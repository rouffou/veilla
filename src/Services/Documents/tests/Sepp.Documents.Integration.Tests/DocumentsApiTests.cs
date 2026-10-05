using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.Contracts;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.Documents.Adapters.Persistence;
using Sepp.Documents.Application.EvaluationSante;

using Shouldly;

using Testcontainers.PostgreSql;

namespace Sepp.Documents.Integration.Tests;

/// <summary>
/// Service complet contre un vrai PostgreSQL éphémère et le stockage local : migrations, modèles, génération PDF/A,
/// chiffrement par zone, intégrité, droits d'accès, audit et consommation de <c>DecisionEmise</c> (ARC-23).
/// </summary>
public sealed class DocumentsApiTests : IAsyncLifetime
{
    private static readonly Guid Affilie = Guid.CreateVersion7();
    private static readonly Guid Personne = Guid.CreateVersion7();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly string _racine = Path.Combine(Path.GetTempPath(), "sepp-documents-tests", Guid.NewGuid().ToString("N"));
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private WebApplicationFactory<Program> _factory = null!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Documents", _postgres.GetConnectionString());
            b.UseSetting("Database:MigrateOnStartup", "true");
            b.UseSetting("Outbox:PollingInterval", "00:00:00.200");
            b.UseSetting("Documents:Adaptateurs:Horodatage", "Simulateur");
            b.UseSetting("Documents:Adaptateurs:Signature", "Simulateur");
            b.UseSetting("Documents:Adaptateurs:SourceLinguistique", "Simulateur");
            b.UseSetting("Documents:Stockage:Type", "Local");
            b.UseSetting("Documents:Stockage:Racine", _racine);
            foreach (var zone in new[] { "standard", "medicale", "psychosociale" })
            {
                b.UseSetting($"Documents:Chiffrement:{zone}:Encryption:CurrentKeyId", $"test-{zone}");
                b.UseSetting($"Documents:Chiffrement:{zone}:Encryption:Keys:test-{zone}", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            }

            b.ConfigureTestServices(s =>
            {
                s.AddAuthentication(TestAuth.Name).AddScheme<AuthenticationSchemeOptions, TestAuth>(TestAuth.Name, _ => { });
                s.PostConfigure<AuthenticationOptions>(o =>
                {
                    o.DefaultAuthenticateScheme = TestAuth.Name;
                    o.DefaultChallengeScheme = TestAuth.Name;
                });
            });
        });
        _ = _factory.Server;
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
        if (Directory.Exists(_racine))
        {
            foreach (var fichier in Directory.EnumerateFiles(_racine, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(fichier, FileAttributes.Normal);
            }

            Directory.Delete(_racine, recursive: true);
        }
    }

    private HttpClient ClientDe(string? userId, Guid? personneId, Guid? affilieId, params string[] roles)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuth.RolesHeader, string.Join(',', roles));
        client.DefaultRequestHeaders.Add(TestAuth.UserHeader, userId ?? "test-user");
        if (personneId is { } p)
        {
            client.DefaultRequestHeaders.Add(TestAuth.PersonneHeader, p.ToString());
        }

        if (affilieId is { } a)
        {
            client.DefaultRequestHeaders.Add(TestAuth.AffilieHeader, a.ToString());
        }

        return client;
    }

    private HttpClient Client(params string[] roles) => ClientDe(null, null, null, roles);

    private async Task<Guid> ModelePublie(string code, string zone, string validateur, string contenu = "# Courrier\n{{nom}}")
    {
        var creation = await Client(Roles.AdministrateurFonctionnel).PostAsJsonAsync("/api/v1/modeles", new
        {
            code,
            langue = "Fr",
            type = "Courrier",
            zone,
            libelle = "Courrier de test",
            contenu,
            champs = new[] { new { nom = "nom", type = "Texte", obligatoire = true, libelle = "Nom" } },
        }, Json, _ct);
        creation.StatusCode.ShouldBe(HttpStatusCode.Created, await creation.Content.ReadAsStringAsync(_ct));
        var id = (await creation.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("id").GetGuid();

        (await Client(validateur).PostAsync($"/api/v1/modeles/{id}/validation", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Client(Roles.AdministrateurFonctionnel).PostAsync($"/api/v1/modeles/{id}/publication", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return id;
    }

    private async Task<JsonElement> Generer(HttpClient client, string code, Guid destinataire, string type = "Personne", bool publier = false)
    {
        var reponse = await client.PostAsJsonAsync("/api/v1/documents", new
        {
            codeModele = code,
            typeDestinataire = type,
            destinataireId = destinataire,
            serviceProprietaire = "test",
            objetType = "objet",
            objetId = Guid.CreateVersion7(),
            valeurs = new { nom = "Marie Dupont" },
            affilieId = Affilie,
            personneId = Personne,
            publier,
        }, Json, _ct);
        reponse.StatusCode.ShouldBe(HttpStatusCode.Created, await reponse.Content.ReadAsStringAsync(_ct));
        return await reponse.Content.ReadFromJsonAsync<JsonElement>(_ct);
    }

    private IReadOnlyCollection<OutboxMessage> Publies => ((InMemoryMessagePublisher)_factory.Services.GetRequiredService<IMessagePublisher>()).Published;

    private async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 50 && !condition(); i++)
        {
            await Task.Delay(100, _ct);
        }

        condition().ShouldBeTrue();
    }

    [Fact]
    public async Task Les_sondes_repondent_et_une_requete_anonyme_est_refusee()
    {
        var anonyme = _factory.CreateClient();

        (await anonyme.GetAsync("/health/ready", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonyme.GetAsync("/api/v1/documents", _ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonyme.GetAsync("/api/v1/modeles", _ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Les_modeles_de_depart_sont_en_brouillon_et_listables()
    {
        var liste = await Client(Roles.AdministrateurFonctionnel).GetFromJsonAsync<JsonElement>("/api/v1/modeles?code=SANTE.EVALUATION.EMPLOYEUR", Json, _ct);

        liste.GetArrayLength().ShouldBe(3);
        liste.EnumerateArray().ShouldAllBe(m => m.GetProperty("statut").GetString() == "Brouillon");
    }

    [Fact]
    public async Task Un_document_est_genere_en_pdf_a_chiffre_puis_relu_et_verifie()
    {
        await ModelePublie("COURRIER.API", "standard", Roles.AdministrateurFonctionnel);
        var document = await Generer(Client(Roles.GestionnaireDossiers), "COURRIER.API", Personne, publier: false);
        var id = document.GetProperty("id").GetGuid();
        document.GetProperty("format").GetString().ShouldBe("PDF/A-1a");

        // Le fichier stocké est chiffré : ni PDF ni texte en clair.
        var fichier = Directory.EnumerateFiles(_racine, "*.enc", SearchOption.AllDirectories).Single();
        var brut = await File.ReadAllBytesAsync(fichier, _ct);
        Encoding.Latin1.GetString(brut).ShouldNotContain("%PDF");
        Encoding.UTF8.GetString(brut).ShouldNotContain("Marie");

        var contenu = await Client(Roles.GestionnaireDossiers).GetAsync($"/api/v1/documents/{id}/contenu", _ct);
        contenu.StatusCode.ShouldBe(HttpStatusCode.OK);
        contenu.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        Encoding.ASCII.GetString((await contenu.Content.ReadAsByteArrayAsync(_ct))[..7]).ShouldBe("%PDF-1.");

        var integrite = await Client(Roles.AdministrateurFonctionnel).GetFromJsonAsync<JsonElement>($"/api/v1/documents/{id}/integrite", Json, _ct);
        integrite.GetProperty("integre").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task L_alteration_du_fichier_stocke_est_detectee_par_l_api_d_integrite_et_la_lecture()
    {
        await ModelePublie("COURRIER.ALTERE", "standard", Roles.AdministrateurFonctionnel);
        var id = (await Generer(Client(Roles.GestionnaireDossiers), "COURRIER.ALTERE", Personne)).GetProperty("id").GetGuid();
        var fichier = Directory.EnumerateFiles(_racine, $"{id}.pdf.enc", SearchOption.AllDirectories).Single();
        File.GetAttributes(fichier).HasFlag(FileAttributes.ReadOnly).ShouldBeTrue("écriture unique émulée : le fichier est en lecture seule");

        // Un attaquant disposant du système de fichiers modifie un octet du contenu chiffré.
        File.SetAttributes(fichier, FileAttributes.Normal);
        var octets = await File.ReadAllBytesAsync(fichier, _ct);
        octets[^3] ^= 0x01;
        await File.WriteAllBytesAsync(fichier, octets, _ct);

        var integrite = await Client(Roles.AdministrateurFonctionnel).GetFromJsonAsync<JsonElement>($"/api/v1/documents/{id}/integrite", Json, _ct);
        integrite.GetProperty("integre").GetBoolean().ShouldBeFalse();
        integrite.GetProperty("conclusion").GetString()!.ShouldContain("altéré");
        (await Client(Roles.GestionnaireDossiers).GetAsync($"/api/v1/documents/{id}/contenu", _ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Un_document_medical_n_est_pas_lisible_sans_permission_et_la_lecture_est_auditee()
    {
        await ModelePublie("MEDICAL.API", "medicale", Roles.CpmtDirigeant);
        var id = (await Generer(Client(Roles.Cpmt), "MEDICAL.API", Personne)).GetProperty("id").GetGuid();

        (await Client(Roles.GestionnaireDossiers).GetAsync($"/api/v1/documents/{id}/contenu", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Client(Roles.AdministrateurFonctionnel).GetAsync($"/api/v1/documents/{id}", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Client(Roles.Cpap).GetAsync($"/api/v1/documents/{id}", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var client = Client(Roles.Cpmt);
        client.DefaultRequestHeaders.Add("X-Motif-Acces", "suivi du dossier");
        (await client.GetAsync($"/api/v1/documents/{id}/contenu", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await Eventually(() => Publies.Any(m => m.EventType == "audit.acces-donnee-sensible.v1"
                                                  && m.Payload.Contains("\"medicale\"", StringComparison.Ordinal)
                                                  && m.Payload.Contains(id.ToString(), StringComparison.Ordinal)
                                                  && m.Payload.Contains("documents", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Le_travailleur_lit_son_document_publie_et_l_employeur_seulement_ceux_de_la_zone_standard()
    {
        await ModelePublie("MEDICAL.PUB", "medicale", Roles.CpmtDirigeant);
        await ModelePublie("COURRIER.PUB", "standard", Roles.AdministrateurFonctionnel);
        var medical = (await Generer(Client(Roles.Cpmt), "MEDICAL.PUB", Personne, publier: true)).GetProperty("id").GetGuid();
        var courrier = (await Generer(Client(Roles.GestionnaireDossiers), "COURRIER.PUB", Affilie, "Affilie", publier: true)).GetProperty("id").GetGuid();

        var travailleur = ClientDe("travailleur-1", Personne, null, Roles.Travailleur);
        (await travailleur.GetAsync($"/api/v1/documents/{medical}/contenu", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var autre = ClientDe("travailleur-2", Guid.CreateVersion7(), null, Roles.Travailleur);
        (await autre.GetAsync($"/api/v1/documents/{medical}/contenu", _ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var employeur = ClientDe("employeur-1", null, Affilie, Roles.Employeur);
        (await employeur.GetAsync($"/api/v1/documents/{courrier}/contenu", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await employeur.GetAsync($"/api/v1/documents/{medical}/contenu", _ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ClientDe("employeur-2", null, Guid.CreateVersion7(), Roles.Employeur).GetAsync($"/api/v1/documents/{courrier}", _ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Un externe ne génère rien.
        (await employeur.PostAsJsonAsync("/api/v1/documents", new { codeModele = "COURRIER.PUB", typeDestinataire = "Affilie", destinataireId = Affilie, objetType = "x", objetId = Guid.CreateVersion7() }, Json, _ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task La_publication_d_un_document_emet_document_publie_sans_contenu()
    {
        await ModelePublie("COURRIER.EVT", "standard", Roles.AdministrateurFonctionnel);
        var id = (await Generer(Client(Roles.GestionnaireDossiers), "COURRIER.EVT", Personne, publier: true)).GetProperty("id").GetGuid();

        await Eventually(() => Publies.Any(m => m.EventType == "documents.document-publie.v1" && m.Payload.Contains(id.ToString(), StringComparison.Ordinal)));

        var message = Publies.Single(m => m.EventType == "documents.document-publie.v1" && m.Payload.Contains(id.ToString(), StringComparison.Ordinal));
        message.Payload.ShouldNotContain("Marie");
        (await Client(Roles.GestionnaireDossiers).PostAsync($"/api/v1/documents/{id}/publication", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task La_signature_qualifiee_simulee_est_enregistree_avec_l_empreinte()
    {
        await ModelePublie("COURRIER.SIGN", "standard", Roles.AdministrateurFonctionnel);
        var document = await Generer(Client(Roles.GestionnaireDossiers), "COURRIER.SIGN", Personne);
        var id = document.GetProperty("id").GetGuid();

        var reponse = await ClientDe("signataire-1", null, null, Roles.GestionnaireDossiers).PostAsync($"/api/v1/documents/{id}/signatures", null, _ct);

        reponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var signature = await reponse.Content.ReadFromJsonAsync<JsonElement>(Json, _ct);
        signature.GetProperty("empreinteSignee").GetString().ShouldBe(document.GetProperty("empreinte").GetString());
        signature.GetProperty("type").GetString().ShouldBe("qualifiee-simulee");
        (await ClientDe("signataire-1", null, null, Roles.GestionnaireDossiers).PostAsync($"/api/v1/documents/{id}/signatures", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Un_modele_avec_une_balise_dangereuse_est_refuse()
    {
        var reponse = await Client(Roles.AdministrateurFonctionnel).PostAsJsonAsync("/api/v1/modeles", new
        {
            code = "DANGER",
            langue = "Fr",
            type = "Courrier",
            zone = "standard",
            libelle = "x",
            contenu = "{{System.IO.File.ReadAllText(\"/etc/passwd\")}}",
            champs = Array.Empty<object>(),
        }, Json, _ct);

        reponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Une_decision_emise_produit_le_formulaire_en_trois_exemplaires_de_facon_idempotente()
    {
        // Les modèles de départ (brouillons) sont validés par le CPMT dirigeant puis publiés, comme en exploitation.
        var modeles = await Client(Roles.AdministrateurFonctionnel).GetFromJsonAsync<JsonElement>("/api/v1/modeles", Json, _ct);
        foreach (var modele in modeles.EnumerateArray().Where(m => m.GetProperty("code").GetString()!.StartsWith("SANTE.EVALUATION", StringComparison.Ordinal)))
        {
            var id = modele.GetProperty("id").GetGuid();
            var valideur = modele.GetProperty("zone").GetString() == "medicale" ? Roles.CpmtDirigeant : Roles.AdministrateurFonctionnel;
            (await Client(valideur).PostAsync($"/api/v1/modeles/{id}/validation", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await Client(Roles.AdministrateurFonctionnel).PostAsync($"/api/v1/modeles/{id}/publication", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var decision = new DecisionEmise(Guid.CreateVersion7(), Personne, Affilie, "APTE_AVEC_MESURES", ["AMENAGEMENT_POSTE"], new DateOnly(2027, 10, 5));
        var message = Guid.CreateVersion7();
        var payload = JsonSerializer.Serialize(decision, EventSerialization.Options);
        var dispatcher = _factory.Services.GetRequiredService<IntegrationEventDispatcher<DocumentsDbContext>>();

        (await dispatcher.DispatchAsync(message, EventContractAttribute.Of(typeof(DecisionEmise)).FullName, payload, _ct)).ShouldBe(1);
        (await dispatcher.DispatchAsync(message, EventContractAttribute.Of(typeof(DecisionEmise)).FullName, payload, _ct)).ShouldBe(0);
        // Même décision rejouée sous un autre identifiant de message : l'exemplaire existe déjà.
        await dispatcher.DispatchAsync(Guid.CreateVersion7(), EventContractAttribute.Of(typeof(DecisionEmise)).FullName, payload, _ct);

        var documents = await Client(Roles.Cpmt).GetFromJsonAsync<JsonElement>($"/api/v1/documents?objetType=decision&objetId={decision.DecisionId}", Json, _ct);
        documents.GetArrayLength().ShouldBe(3);
        documents.EnumerateArray().Select(d => d.GetProperty("exemplaire").GetString()).Order().ShouldBe(["dossier", "employeur", "travailleur"]);
        documents.EnumerateArray().Single(d => d.GetProperty("exemplaire").GetString() == "employeur").GetProperty("zone").GetString().ShouldBe("standard");

        // L'employeur lit son exemplaire, sans accès à ceux du travailleur ni du dossier.
        var employeur = ClientDe("employeur-1", null, Affilie, Roles.Employeur);
        var exemplaireEmployeur = documents.EnumerateArray().Single(d => d.GetProperty("exemplaire").GetString() == "employeur").GetProperty("id").GetGuid();
        var exemplaireDossier = documents.EnumerateArray().Single(d => d.GetProperty("exemplaire").GetString() == "dossier").GetProperty("id").GetGuid();
        (await employeur.GetAsync($"/api/v1/documents/{exemplaireEmployeur}/contenu", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await employeur.GetAsync($"/api/v1/documents/{exemplaireDossier}/contenu", _ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await Eventually(() => Publies.Count(m => m.EventType == "documents.document-publie.v1") == 2);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
        (await db.Documents.CountAsync(d => d.ObjetId == decision.DecisionId, _ct)).ShouldBe(3);
    }

    /// <summary>Authentification de test : rôles, utilisateur et périmètre passés dans des en-têtes.</summary>
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "Test";
        public const string RolesHeader = "X-Test-Roles";
        public const string UserHeader = "X-Test-User";
        public const string PersonneHeader = "X-Test-Personne";
        public const string AffilieHeader = "X-Test-Affilie";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RolesHeader, out var roles))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(r => new Claim("roles", r))
                .Append(new Claim("sub", Request.Headers.TryGetValue(UserHeader, out var user) ? user.ToString() : "test-user"))
                .ToList();
            if (Request.Headers.TryGetValue(PersonneHeader, out var personne))
            {
                claims.Add(new Claim("personne_id", personne.ToString()));
            }

            if (Request.Headers.TryGetValue(AffilieHeader, out var affilie))
            {
                claims.Add(new Claim("affilie_id", affilie.ToString()));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Name, "sub", "roles"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Name)));
        }
    }
}
