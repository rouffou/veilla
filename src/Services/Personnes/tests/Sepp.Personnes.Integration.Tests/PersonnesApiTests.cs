using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.Personnes.Application;
using Sepp.Personnes.Application.Imports;
using Sepp.Personnes.Application.Mutations;
using Sepp.Personnes.Application.Occupations;
using Sepp.Personnes.Application.Personnes;
using Sepp.Personnes.Domain.Personnes;

using Shouldly;

namespace Sepp.Personnes.Integration.Tests;

/// <summary>Migrations, chiffrement, index aveugle, outbox, API et autorisations contre un vrai PostgreSQL.</summary>
public sealed class PersonnesApiTests(PersonnesApiFixture fixture) : IClassFixture<PersonnesApiFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static int _ordre = 100;

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly Guid _affilie = Guid.CreateVersion7();

    /// <summary>NISS valide et unique par test (personne née le 30/07/1985).</summary>
    private static string NouveauNiss()
    {
        var ordre = Interlocked.Increment(ref _ordre);
        var corps = long.Parse($"850730{ordre:D3}");
        return $"850730{ordre:D3}{97 - (corps % 97):D2}";
    }

    private static object Identite(string nom = "Dupont") =>
        new { nom, prenom = "Marie", dateNaissance = "1985-07-30", sexe = "Feminin", langue = "Fr" };

    private async Task<PersonneEnregistreeDto> Creer(string niss, HttpClient? client = null)
    {
        var response = await (client ?? fixture.Client(Roles.GestionnaireDossiers)).PostAsJsonAsync("/api/v1/personnes", new
        {
            niss,
            identite = Identite(),
            coordonnees = new
            {
                adresse = new { rue = "Rue de la Loi", numero = "16", codePostal = "1000", localite = "Bruxelles" },
                email = "marie@example.test",
                canalPrefere = "Courrier",
            },
            occupation = new { affilieId = _affilie, typeTravailleur = "Salarie", typeContrat = "DureeIndeterminee", dateDebut = "2026-01-01" },
        }, _ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(_ct));
        return (await response.Content.ReadFromJsonAsync<PersonneEnregistreeDto>(Json, _ct))!;
    }

    [Fact]
    public async Task Les_sondes_de_sante_repondent_et_une_requete_anonyme_est_refusee()
    {
        var client = fixture.Factory.CreateClient();
        (await client.GetAsync("/health/ready", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync($"/api/v1/personnes/{Guid.CreateVersion7()}", _ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Le_niss_n_est_jamais_stocke_en_clair_et_reste_recherchable()
    {
        var niss = NouveauNiss();
        var creee = await Creer(niss);

        // Recherche par POST (le NISS n'est jamais dans l'URL), sous une forme formatée.
        var formate = $"{niss[..2]}.{niss[2..4]}.{niss[4..6]}-{niss[6..9]}.{niss[9..]}";
        var recherche = await fixture.Client(Roles.Cpmt).PostAsJsonAsync("/api/v1/personnes/recherche", new { niss = formate }, _ct);
        recherche.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await recherche.Content.ReadFromJsonAsync<PersonneResumeDto>(Json, _ct))!.Id.ShouldBe(creee.PersonneId);

        var fiche = await fixture.Client(Roles.Cpmt).GetStringAsync($"/api/v1/personnes/{creee.PersonneId}", _ct);
        fiche.ShouldNotContain(niss);
        fiche.ShouldContain($"XX.XX.XX-XXX.{niss[9..]}");

        // SQL brut : la colonne ne contient que le texte chiffré, l'index est un HMAC.
        await using var connexion = new NpgsqlConnection(fixture.ConnectionString);
        await connexion.OpenAsync(_ct);
        await using (var commande = new NpgsqlCommand("SELECT niss_chiffre, niss_hash FROM personne WHERE id = @id", connexion))
        {
            commande.Parameters.AddWithValue("id", creee.PersonneId);
            await using var lecteur = await commande.ExecuteReaderAsync(_ct);
            (await lecteur.ReadAsync(_ct)).ShouldBeTrue();
            var chiffre = lecteur.GetString(0);
            var hash = lecteur.GetString(1);
            chiffre.ShouldStartWith("v1:test:");
            chiffre.ShouldNotContain(niss);
            hash.Length.ShouldBe(64);
            hash.ShouldNotContain(niss);
        }

        // Aucune ligne d'aucune table (données, outbox, inbox) ne contient le NISS.
        (await OccurrencesEnBase(connexion, niss)).ShouldBe(0);
    }

    [Fact]
    public async Task Les_evenements_de_l_outbox_et_les_journaux_ne_contiennent_pas_le_niss()
    {
        var niss = NouveauNiss();
        var creee = await Creer(niss);
        var invalide = niss[..10] + (niss[10] == '0' ? '1' : '0');
        var gestionnaire = fixture.Client(Roles.GestionnaireDossiers);
        (await gestionnaire.PostAsJsonAsync("/api/v1/personnes/recherche", new { niss }, _ct)).EnsureSuccessStatusCode();
        var refus = await gestionnaire.PostAsJsonAsync("/api/v1/personnes/recherche", new { niss = invalide }, _ct);
        refus.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refus.Content.ReadAsStringAsync(_ct)).ShouldNotContain(invalide);

        var publisher = (InMemoryMessagePublisher)fixture.Factory.Services.GetRequiredService<IMessagePublisher>();
        await Eventually(() => publisher.Published.Any(m => m.EventType == "personnes.occupation-debutee.v1" && m.Payload.Contains(creee.PersonneId.ToString())));

        publisher.Published.ShouldAllBe(m => !m.Payload.Contains(niss));
        fixture.Journal.Entrees.ShouldNotBeEmpty();
        fixture.Journal.Entrees.Where(e => e.Contains(niss)).ShouldBeEmpty();
        fixture.Journal.Entrees.Where(e => e.Contains(invalide)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Une_grossesse_est_chiffree_en_base_et_publiee_sous_une_categorie_generique()
    {
        var creee = await Creer(NouveauNiss());
        var employeur = fixture.Client(Roles.Employeur, _affilie);

        var response = await employeur.PostAsJsonAsync($"/api/v1/personnes/{creee.PersonneId}/etats-particuliers",
            new { type = "Grossesse", dateDebut = "2026-03-01" }, _ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(_ct));

        var publisher = (InMemoryMessagePublisher)fixture.Factory.Services.GetRequiredService<IMessagePublisher>();
        await Eventually(() => publisher.Published.Any(m => m.EventType == "personnes.etat-particulier-declare.v1" && m.Payload.Contains(creee.PersonneId.ToString())));
        var message = publisher.Published.Single(m => m.EventType == "personnes.etat-particulier-declare.v1" && m.Payload.Contains(creee.PersonneId.ToString()));
        message.Payload.ShouldContain("PROTECTION_MATERNITE");
        message.Payload.ShouldNotContain("rossesse");

        await using var connexion = new NpgsqlConnection(fixture.ConnectionString);
        await connexion.OpenAsync(_ct);
        await using var commande = new NpgsqlCommand("SELECT count(*) FROM etat_particulier e WHERE e::text ILIKE '%grossesse%'", connexion);
        ((long)(await commande.ExecuteScalarAsync(_ct))!).ShouldBe(0);

        // Le déclarant relit sa déclaration ; un conseiller en prévention sécurité n'y a pas accès.
        (await employeur.GetStringAsync($"/api/v1/personnes/{creee.PersonneId}/etats-particuliers", _ct)).ShouldContain("Grossesse");
        (await fixture.Client(Roles.ConseillerSecurite).GetAsync($"/api/v1/personnes/{creee.PersonneId}/etats-particuliers", _ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task L_employeur_est_limite_a_son_affilie()
    {
        var creee = await Creer(NouveauNiss());

        (await fixture.Client(Roles.Employeur, _affilie).GetAsync($"/api/v1/personnes/{creee.PersonneId}", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await fixture.Client(Roles.Employeur, Guid.CreateVersion7()).GetAsync($"/api/v1/personnes/{creee.PersonneId}", _ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await fixture.Client(Roles.Employeur).GetAsync($"/api/v1/personnes/{creee.PersonneId}", _ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await fixture.Client(Roles.Employeur, _affilie).PutAsJsonAsync($"/api/v1/personnes/{creee.PersonneId}/identite", Identite("Autre"), _ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.Client(Roles.Travailleur).GetAsync($"/api/v1/personnes/{creee.PersonneId}", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var liste = await fixture.Client(Roles.Employeur, _affilie)
            .GetFromJsonAsync<List<PersonneResumeDto>>($"/api/v1/affilies/{_affilie}/travailleurs?date=2026-06-01", Json, _ct);
        liste!.ShouldHaveSingleItem().Id.ShouldBe(creee.PersonneId);
    }

    [Fact]
    public async Task Les_affectations_sont_historisees_et_cloturees_a_la_sortie()
    {
        var creee = await Creer(NouveauNiss());
        var client = fixture.Client(Roles.GestionnaireDossiers);
        var poste = Guid.CreateVersion7();
        var site = Guid.CreateVersion7();

        var affectation = await client.PostAsJsonAsync($"/api/v1/personnes/{creee.PersonneId}/occupations/{creee.OccupationId}/affectations",
            new { posteId = poste, siteId = site, valideDu = "2026-01-01" }, _ct);
        affectation.StatusCode.ShouldBe(HttpStatusCode.Created);
        var affectationId = (await affectation.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("id").GetGuid();

        (await client.PostAsJsonAsync($"/api/v1/personnes/{creee.PersonneId}/affectations/{affectationId}/changement",
            new { posteId = Guid.CreateVersion7(), siteId = site, aPartirDu = "2026-04-01" }, _ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await client.PostAsJsonAsync($"/api/v1/personnes/{creee.PersonneId}/occupations/{creee.OccupationId}/fin",
            new { dateFin = "2026-06-30" }, _ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var historique = await client.GetFromJsonAsync<List<AffectationDto>>($"/api/v1/personnes/{creee.PersonneId}/affectations", Json, _ct);
        historique!.Count.ShouldBe(2);
        historique[0].ValideJusquAu.ShouldBe(new DateOnly(2026, 4, 1));
        historique[1].ValideJusquAu.ShouldBe(new DateOnly(2026, 7, 1));
        (await client.GetFromJsonAsync<List<AffectationDto>>($"/api/v1/personnes/{creee.PersonneId}/affectations?date=2026-05-01", Json, _ct))!
            .ShouldHaveSingleItem().PosteId.ShouldNotBe(poste);

        var publisher = (InMemoryMessagePublisher)fixture.Factory.Services.GetRequiredService<IMessagePublisher>();
        await Eventually(() => publisher.Published.Count(m => m.EventType == "personnes.affectation-modifiee.v1" && m.Payload.Contains(creee.PersonneId.ToString())) == 4);
    }

    [Fact]
    public async Task L_alimentation_dimona_est_idempotente()
    {
        var client = fixture.Client(Roles.GestionnaireDossiers);
        var niss = NouveauNiss();
        var reference = $"DIM{Guid.CreateVersion7():N}"[..20];
        var entree = new
        {
            referenceDimona = reference,
            niss,
            identite = Identite(),
            affilieId = _affilie,
            typeTravailleur = "Etudiant",
            typeContrat = "Etudiant",
            dateDebut = "2026-07-01",
            dateFin = "2026-08-31",
        };

        var premiere = await (await client.PostAsJsonAsync("/api/v1/dimona/entrees", entree, _ct)).Content.ReadFromJsonAsync<DimonaEnregistreeDto>(Json, _ct);
        var rejeu = await (await client.PostAsJsonAsync("/api/v1/dimona/entrees", entree, _ct)).Content.ReadFromJsonAsync<DimonaEnregistreeDto>(Json, _ct);

        premiere!.DejaEnregistree.ShouldBeFalse();
        rejeu!.DejaEnregistree.ShouldBeTrue();
        rejeu.OccupationId.ShouldBe(premiere.OccupationId);
        (await client.PostAsJsonAsync($"/api/v1/dimona/{reference}/sortie", new { dateFin = "2026-08-31" }, _ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.Client(Roles.Employeur, _affilie).PostAsJsonAsync("/api/v1/dimona/entrees", entree, _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var occupations = await client.GetFromJsonAsync<List<OccupationDto>>($"/api/v1/personnes/{premiere.PersonneId}/occupations", Json, _ct);
        occupations!.ShouldHaveSingleItem().ReferenceDimona.ShouldBe(reference.ToUpperInvariant());
    }

    [Fact]
    public async Task Le_compte_technique_integrations_alimente_dimona_sans_lire_les_fiches()
    {
        var integrations = fixture.Client(Roles.Integrations);
        var reference = $"DIM{Guid.CreateVersion7():N}"[..20];
        var entree = new
        {
            referenceDimona = reference,
            niss = NouveauNiss(),
            identite = Identite(),
            affilieId = _affilie,
            typeTravailleur = "Salarie",
            typeContrat = "DureeIndeterminee",
            dateDebut = "2026-09-01",
        };

        var reponse = await integrations.PostAsJsonAsync("/api/v1/dimona/entrees", entree, _ct);
        reponse.StatusCode.ShouldBe(HttpStatusCode.OK, await reponse.Content.ReadAsStringAsync(_ct));
        var enregistree = (await reponse.Content.ReadFromJsonAsync<DimonaEnregistreeDto>(Json, _ct))!;
        (await integrations.PostAsJsonAsync($"/api/v1/dimona/{reference}/sortie", new { dateFin = "2026-12-31" }, _ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Rôle technique limité à l'écriture DIMONA : aucune lecture des fiches des travailleurs.
        (await integrations.GetAsync($"/api/v1/personnes/{enregistree.PersonneId}", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await integrations.PostAsJsonAsync("/api/v1/personnes/recherche", new { niss = entree.niss }, _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static object Mutation(string niss, string reference, string type, string effet, object? adresse = null, string? nom = null, string? prenom = null, string? langue = null) =>
        new { referenceMutation = reference, niss, type, dateEffet = effet, adresse, nom, prenom, langue };

    [Fact]
    public async Task Les_mutations_du_registre_national_sont_appliquees_historisees_chiffrees_et_idempotentes()
    {
        var niss = NouveauNiss();
        var creee = await Creer(niss);
        var integrations = fixture.Client(Roles.Integrations);
        var prefixe = $"RN{Guid.CreateVersion7():N}"[..16];

        async Task<MutationEnregistreeDto> Envoyer(object mutation)
        {
            var reponse = await integrations.PostAsJsonAsync("/api/v1/registre-national/mutations", mutation, _ct);
            reponse.StatusCode.ShouldBe(HttpStatusCode.OK, await reponse.Content.ReadAsStringAsync(_ct));
            return (await reponse.Content.ReadFromJsonAsync<MutationEnregistreeDto>(Json, _ct))!;
        }

        var adresse = new { rue = "Avenue Louise", numero = "1", boite = "B", codePostal = "1050", localite = "Ixelles" };
        (await Envoyer(Mutation(niss, $"{prefixe}-1", "ChangementAdresse", "2026-09-01", adresse: adresse))).Statut.ShouldBe(StatutMutation.Appliquee);
        (await Envoyer(Mutation(niss, $"{prefixe}-2", "ChangementNom", "2026-09-01", nom: "Zwanenburg"))).Statut.ShouldBe(StatutMutation.Appliquee);
        (await Envoyer(Mutation(niss, $"{prefixe}-3", "ChangementPrenom", "2026-09-01", prenom: "Annelies"))).Statut.ShouldBe(StatutMutation.Appliquee);
        (await Envoyer(Mutation(niss, $"{prefixe}-4", "ChangementLangue", "2026-09-01", langue: "Nl"))).Statut.ShouldBe(StatutMutation.Appliquee);

        // Idempotence : le rejeu, même après d'autres mutations, ne change rien.
        var rejeu = await Envoyer(Mutation(niss, $"{prefixe}-2", "ChangementNom", "2026-09-01", nom: "Zwanenburg"));
        rejeu.ShouldBe(new MutationEnregistreeDto(creee.PersonneId, StatutMutation.DejaAppliquee));

        var fiche = (await fixture.Client(Roles.Cpmt).GetFromJsonAsync<PersonneDto>($"/api/v1/personnes/{creee.PersonneId}", Json, _ct))!;
        (fiche.Nom, fiche.Prenom, fiche.Langue.ToString(), fiche.Adresse!.Rue).ShouldBe(("Zwanenburg", "Annelies", "Nl", "Avenue Louise"));

        // Même référence pour une autre mutation, ou pour une autre personne : refus (409 / 400), rien ne change.
        var autreType = await integrations.PostAsJsonAsync("/api/v1/registre-national/mutations", Mutation(niss, $"{prefixe}-2", "ChangementPrenom", "2026-09-01", prenom: "Eva"), _ct);
        autreType.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var autre = await Creer(NouveauNiss());
        var nissAutre = (await fixture.Client(Roles.Cpmt).GetFromJsonAsync<PersonneDto>($"/api/v1/personnes/{autre.PersonneId}", Json, _ct))!;
        nissAutre.Nom.ShouldBe("Dupont");

        // SQL brut : l'historique (DAT-04) existe, ses valeurs avant / après sont chiffrées, aucun NISS ni valeur d'identité en clair.
        await using var connexion = new NpgsqlConnection(fixture.ConnectionString);
        await connexion.OpenAsync(_ct);
        await using (var commande = new NpgsqlCommand(
                         "SELECT reference, type, avant_chiffre, apres_chiffre, created_by FROM mutation_registre_national WHERE personne_id = @id ORDER BY reference", connexion))
        {
            commande.Parameters.AddWithValue("id", creee.PersonneId);
            await using var lecteur = await commande.ExecuteReaderAsync(_ct);
            var lignes = 0;
            while (await lecteur.ReadAsync(_ct))
            {
                lignes++;
                lecteur.GetString(2).ShouldStartWith("v1:test:");
                lecteur.GetString(3).ShouldStartWith("v1:test:");
                lecteur.GetString(4).ShouldBe("test-user");
            }

            lignes.ShouldBe(4);
        }

        (await OccurrencesEnBase(connexion, niss)).ShouldBe(0);
        foreach (var valeur in new[] { "Zwanenburg", "Annelies", "Avenue Louise", "Ixelles" })
        {
            await using var presence = new NpgsqlCommand("SELECT count(*) FROM mutation_registre_national t WHERE t::text LIKE @motif", connexion);
            presence.Parameters.AddWithValue("motif", $"%{valeur}%");
            ((long)(await presence.ExecuteScalarAsync(_ct))!).ShouldBe(0, valeur);
        }

        // ARC-06 : aucun événement ne naît d'un changement d'identité ; aucun NISS ni nom dans les journaux.
        var publisher = (InMemoryMessagePublisher)fixture.Factory.Services.GetRequiredService<IMessagePublisher>();
        publisher.Published.ShouldAllBe(m => !m.Payload.Contains(niss) && !m.Payload.Contains("Zwanenburg") && !m.Payload.Contains("Ixelles"));
        fixture.Journal.Entrees.Where(e => e.Contains(niss)).ShouldBeEmpty();
        fixture.Journal.Entrees.Where(e => e.Contains("Zwanenburg") || e.Contains("Annelies")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_deces_cloture_les_occupations_et_publie_occupation_terminee_sans_donnee_d_identite()
    {
        var niss = NouveauNiss();
        var creee = await Creer(niss);
        var integrations = fixture.Client(Roles.Integrations);
        var reference = $"RN{Guid.CreateVersion7():N}"[..16];
        var mutation = Mutation(niss, reference, "Deces", "2026-09-15");

        var reponse = await integrations.PostAsJsonAsync("/api/v1/registre-national/mutations", mutation, _ct);
        reponse.StatusCode.ShouldBe(HttpStatusCode.OK, await reponse.Content.ReadAsStringAsync(_ct));
        (await reponse.Content.ReadFromJsonAsync<MutationEnregistreeDto>(Json, _ct))!.Statut.ShouldBe(StatutMutation.Appliquee);
        var rejeu = await (await integrations.PostAsJsonAsync("/api/v1/registre-national/mutations", mutation, _ct)).Content.ReadFromJsonAsync<MutationEnregistreeDto>(Json, _ct);
        rejeu!.Statut.ShouldBe(StatutMutation.DejaAppliquee);

        var occupations = (await fixture.Client(Roles.Cpmt).GetFromJsonAsync<List<OccupationDto>>($"/api/v1/personnes/{creee.PersonneId}/occupations", Json, _ct))!;
        var occupation = occupations.ShouldHaveSingleItem();
        occupation.DateFin.ShouldBe(new DateOnly(2026, 9, 15));

        var publisher = (InMemoryMessagePublisher)fixture.Factory.Services.GetRequiredService<IMessagePublisher>();
        await Eventually(() => publisher.Published.Any(m => m.EventType == "personnes.occupation-terminee.v1" && m.Payload.Contains(occupation.Id.ToString())));
        var evenements = publisher.Published.Where(m => m.EventType == "personnes.occupation-terminee.v1" && m.Payload.Contains(occupation.Id.ToString())).ToList();
        evenements.ShouldHaveSingleItem().Payload.ShouldContain("2026-09-15");
        evenements.ShouldAllBe(m => !m.Payload.Contains(niss) && !m.Payload.Contains("Dupont") && !m.Payload.Contains("Marie"));

        await using var connexion = new NpgsqlConnection(fixture.ConnectionString);
        await connexion.OpenAsync(_ct);
        (await OccurrencesEnBase(connexion, niss)).ShouldBe(0);
        fixture.Journal.Entrees.Where(e => e.Contains(niss)).ShouldBeEmpty();
    }

    [Fact]
    public async Task L_api_des_mutations_est_reservee_au_compte_technique_et_tolere_une_personne_inconnue()
    {
        var niss = NouveauNiss();
        await Creer(niss);
        var mutation = Mutation(niss, $"RN{Guid.CreateVersion7():N}"[..16], "ChangementNom", "2026-09-01", nom: "Martin");

        (await fixture.Factory.CreateClient().PostAsJsonAsync("/api/v1/registre-national/mutations", mutation, _ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await fixture.Client(Roles.Employeur, _affilie).PostAsJsonAsync("/api/v1/registre-national/mutations", mutation, _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.Client(Roles.Cpmt).PostAsJsonAsync("/api/v1/registre-national/mutations", mutation, _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var integrations = fixture.Client(Roles.Integrations);
        var invalide = await integrations.PostAsJsonAsync("/api/v1/registre-national/mutations", Mutation(niss[..10] + "0", "RN-X", "Deces", "2026-09-01"), _ct);
        invalide.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await invalide.Content.ReadAsStringAsync(_ct)).ShouldNotContain(niss[..10]);
        (await integrations.PostAsJsonAsync("/api/v1/registre-national/mutations", Mutation(niss, "RN-Y", "ChangementNom", "2026-09-01"), _ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var inconnue = await integrations.PostAsJsonAsync("/api/v1/registre-national/mutations", Mutation(NouveauNiss(), $"RN{Guid.CreateVersion7():N}"[..16], "Deces", "2026-09-01"), _ct);
        inconnue.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await inconnue.Content.ReadFromJsonAsync<MutationEnregistreeDto>(Json, _ct))!.ShouldBe(new MutationEnregistreeDto(null, StatutMutation.PersonneInconnue));
    }

    [Fact]
    public async Task L_import_csv_produit_un_rapport_ligne_par_ligne()
    {
        var existant = NouveauNiss();
        await Creer(existant);
        var nouveau = NouveauNiss();
        var csv = "﻿NISS;Nom;Prénom;Date naissance;Sexe;Langue;Date début;Email\r\n"
                  + $"{nouveau};\"Van Damme; dit JC\";Jean;30/07/1985;M;nl;2026-02-01;jean@example.test\r\n"
                  + "85073000000;Durand;Paul;30/07/1985;M;fr;2026-02-01;\r\n"
                  + $"{nouveau};Van Damme;Jean;30/07/1985;M;nl;2026-02-01;\r\n"
                  + $"{existant};Dupont;Marie;30/07/1985;F;fr;2026-02-01;\r\n";
        var client = fixture.Client(Roles.Employeur, _affilie);
        var autreAffilie = Guid.CreateVersion7();

        (await client.PostAsync($"/api/v1/affilies/{autreAffilie}/imports/travailleurs", new StringContent(csv, Encoding.UTF8, "text/csv"), _ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var simulation = await (await client.PostAsync($"/api/v1/affilies/{_affilie}/imports/travailleurs?simulation=true",
            new StringContent(csv, Encoding.UTF8, "text/csv"), _ct)).Content.ReadFromJsonAsync<RapportImportDto>(Json, _ct);
        simulation!.PersonnesCreees.ShouldBe(1);

        var response = await client.PostAsync($"/api/v1/affilies/{_affilie}/imports/travailleurs", new StringContent(csv, Encoding.UTF8, "text/csv"), _ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(_ct));
        var rapport = (await response.Content.ReadFromJsonAsync<RapportImportDto>(Json, _ct))!;

        rapport.Lignes.Select(l => (l.Ligne, l.Statut)).ShouldBe(
        [
            (2, StatutLigneImport.PersonneCreee),
            (3, StatutLigneImport.Erreur),
            (4, StatutLigneImport.Erreur),
            (5, StatutLigneImport.Erreur),
        ]);
        rapport.Lignes[3].Erreurs.ShouldHaveSingleItem().ShouldContain("Doublon");
        (await response.Content.ReadAsStringAsync(_ct)).ShouldNotContain(nouveau);

        var cree = await fixture.Client(Roles.Cpmt).GetFromJsonAsync<PersonneDto>($"/api/v1/personnes/{rapport.Lignes[0].PersonneId}", Json, _ct);
        cree!.Nom.ShouldBe("Van Damme; dit JC");
    }

    private static async Task<long> OccurrencesEnBase(NpgsqlConnection connexion, string valeur)
    {
        // Liste fermée des tables : un test échoue si une nouvelle table n'est pas ajoutée à la vérification.
        await using (var tables = new NpgsqlCommand(
                         "SELECT string_agg(table_name, ',' ORDER BY table_name) FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'",
                         connexion))
        {
            ((string)(await tables.ExecuteScalarAsync())!).ShouldBe("__EFMigrationsHistory,affectation,etat_particulier,inbox_message,mutation_registre_national,occupation,outbox_message,personne");
        }

        await using var commande = new NpgsqlCommand(
            """
            SELECT (SELECT count(*) FROM personne t WHERE t::text LIKE @motif)
                 + (SELECT count(*) FROM occupation t WHERE t::text LIKE @motif)
                 + (SELECT count(*) FROM affectation t WHERE t::text LIKE @motif)
                 + (SELECT count(*) FROM etat_particulier t WHERE t::text LIKE @motif)
                 + (SELECT count(*) FROM mutation_registre_national t WHERE t::text LIKE @motif)
                 + (SELECT count(*) FROM outbox_message t WHERE t::text LIKE @motif)
                 + (SELECT count(*) FROM inbox_message t WHERE t::text LIKE @motif)
            """,
            connexion);
        commande.Parameters.AddWithValue("motif", $"%{valeur}%");
        return (long)(await commande.ExecuteScalarAsync())!;
    }

    private async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 50 && !condition(); i++)
        {
            await Task.Delay(100, _ct);
        }

        condition().ShouldBeTrue();
    }
}
