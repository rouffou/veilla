using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.Contracts;
using Sepp.Contracts.Affilies;
using Sepp.Integrations.Adapters.External;
using Sepp.Integrations.Adapters.Persistence;
using Sepp.Integrations.Adapters.Planification;
using Sepp.Integrations.Application.Correspondances;
using Sepp.Integrations.Application.Flux;
using Sepp.Integrations.Domain;
using Sepp.Integrations.Domain.Flux;

using Shouldly;

namespace Sepp.Integrations.Integration.Tests;

/// <summary>
/// Flux DIMONA de bout en bout (AFF-20) : simulateur BCSS, journal, appel HTTP authentifié du service Personnes
/// (simulé), panne, relance manuelle idempotente, purge, et absence de NISS en clair en base comme dans les journaux.
/// Fixture dédiée : le simulateur DIMONA ne livre qu'un lot, pour les premiers affiliés connus.
/// </summary>
public sealed class FluxDimonaApiTests(IntegrationsApiFixture fixture) : IClassFixture<IntegrationsApiFixture>
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Plus petit numéro BCE valide : premier affilié servi par le simulateur.</summary>
    private static readonly string Employeur = NumerosBce.AvecControle("00000000");

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private HttpClient Gestionnaire => fixture.Client(Roles.GestionnaireDossiers);

    [Fact]
    public async Task Le_flux_dimona_alimente_personnes_journalise_se_relance_et_ne_stocke_aucun_niss_en_clair()
    {
        // §15.3 : la correspondance numéro BCE ↔ affilié est apprise de l'événement AffilieCree (consommateur idempotent).
        var affilie = Guid.CreateVersion7();
        var message = Guid.CreateVersion7();
        (await Dispatcher(message, new AffilieCree(affilie, Employeur, "A"))).ShouldBe(1);
        (await Dispatcher(message, new AffilieCree(affilie, Employeur, "A"))).ShouldBe(0);

        var declarations = SimulateurDimona.Declarations(Employeur);
        var nissSalarie = declarations[0].Niss;
        var nissEtudiant = declarations[1].Niss;
        var nissNonAffilie = DonneesFictives.Niss(DonneesFictives.Graine(SimulateurDimona.EmployeurNonAffilie));

        // 1. Personnes refuse l'accès : les échanges restent en erreur (relançables), l'employeur non affilié est rejeté.
        fixture.Personnes.Panne = HttpStatusCode.Forbidden;
        var rapport = await Lancer("dimona");
        rapport.ShouldBe(new RapportExecutionDto(TypeFlux.Dimona, 4, 0, 0, 1, 3, 0, null));
        var journal = await Journal("?flux=dimona&taille=200");
        journal.Elements.Count.ShouldBe(4);
        var rejet = journal.Elements.Single(e => e.Statut == StatutEchange.Rejete);
        rejet.CodeErreur.ShouldBe("integrations.employeur-inconnu");
        var enErreur = journal.Elements.Where(e => e.Statut == StatutEchange.EnErreur).OrderBy(e => e.RecuLe).ThenBy(e => e.Id).ToList();
        enErreur.ShouldAllBe(e => e.CodeErreur == "integrations.personnes-acces-refuse" && e.Relancable);
        (await Gestionnaire.GetFromJsonAsync<PageDto<EchangeFluxDto>>("/api/v1/flux/erreurs?flux=dimona", Json, _ct))!.Total.ShouldBe(4);

        // 2. Relance manuelle après rétablissement : chaque échange aboutit ; une seconde relance est sans effet.
        fixture.Personnes.Panne = null;
        foreach (var echange in enErreur)
        {
            var relance = await Gestionnaire.PostAsync($"/api/v1/flux/journal/{echange.Id}/relance", null, _ct);
            relance.StatusCode.ShouldBe(HttpStatusCode.OK, await relance.Content.ReadAsStringAsync(_ct));
            var resultat = (await relance.Content.ReadFromJsonAsync<EchangeFluxDto>(Json, _ct))!;
            resultat.Statut.ShouldBe(StatutEchange.Traite);
            resultat.Tentatives.ShouldBe(2);
        }

        var appelsAvantRejeu = fixture.Personnes.AppelsPersonnes.Count;
        var rejeu = await (await Gestionnaire.PostAsync($"/api/v1/flux/journal/{enErreur[0].Id}/relance", null, _ct)).Content.ReadFromJsonAsync<EchangeFluxDto>(Json, _ct);
        rejeu!.Statut.ShouldBe(StatutEchange.Traite);
        rejeu.Tentatives.ShouldBe(2);
        fixture.Personnes.AppelsPersonnes.Count.ShouldBe(appelsAvantRejeu);

        // 3. Une nouvelle exécution ne rejournalise rien (position du flux et clé d'idempotence).
        (await Lancer("dimona")).Recus.ShouldBe(0);

        // 4. Appels HTTP internes : jeton client credentials (obtenu une fois, mis en cache), NISS dans le corps uniquement.
        var appels = fixture.Personnes.AppelsPersonnes;
        appels.ShouldAllBe(a => a.Autorisation == "Bearer jeton-test");
        appels.ShouldAllBe(a => !a.Chemin.Contains(nissSalarie) && !a.Chemin.Contains(nissEtudiant));
        fixture.Personnes.JetonsDelivres.ShouldBe(1);
        var entrees = appels.Where(a => a.Chemin == "/api/v1/dimona/entrees" && a.Corps.Contains(nissEtudiant)).ToList();
        entrees.ShouldNotBeEmpty();
        using (var corps = JsonDocument.Parse(entrees[^1].Corps))
        {
            var racine = corps.RootElement;
            racine.GetProperty("affilieId").GetGuid().ShouldBe(affilie);
            racine.GetProperty("typeTravailleur").GetString().ShouldBe("Etudiant");
            racine.GetProperty("dateFin").GetString().ShouldBe("2026-08-31");
            racine.GetProperty("identite").GetProperty("nom").GetString()!.ShouldStartWith("Simule-");
        }

        appels.ShouldContain(a => a.Chemin == $"/api/v1/dimona/SIM{Employeur}01/sortie" && a.Corps.Contains("2026-06-30"));

        // Contrat avec Personnes : la référence DIMONA est alphanumérique, 50 caractères au plus (constaté en bout en bout).
        foreach (var entree in appels.Where(a => a.Chemin == "/api/v1/dimona/entrees"))
        {
            using var corps = JsonDocument.Parse(entree.Corps);
            var reference = corps.RootElement.GetProperty("referenceDimona").GetString()!;
            (reference.Length <= 50 && reference.All(char.IsAsciiLetterOrDigit)).ShouldBeTrue($"Référence DIMONA refusée par Personnes : {reference}");
        }

        // 5. Correspondances référence DIMONA ↔ occupation, sans NISS.
        var correspondances = await Gestionnaire.GetFromJsonAsync<List<CorrespondanceDto>>("/api/v1/correspondances?type=ReferenceDimona", Json, _ct);
        correspondances!.Select(c => c.ValeurExterne).ShouldBe([$"SIM{Employeur}01", $"SIM{Employeur}02"], ignoreOrder: true);

        // 6. SQL brut : la charge utile n'existe que chiffrée ; aucune ligne d'aucune table ne contient un NISS.
        await using var connexion = new NpgsqlConnection(fixture.ConnectionString);
        await connexion.OpenAsync(_ct);
        await using (var commande = new NpgsqlCommand("SELECT charge_utile_chiffree FROM journal_flux WHERE flux = 'Dimona' AND charge_utile_chiffree IS NOT NULL", connexion))
        {
            await using var lecteur = await commande.ExecuteReaderAsync(_ct);
            var chiffrees = 0;
            while (await lecteur.ReadAsync(_ct))
            {
                lecteur.GetString(0).ShouldStartWith("v1:test:");
                chiffrees++;
            }

            chiffrees.ShouldBe(4);
        }

        fixture.Journal.Entrees.ShouldContain(e => e.Contains("/api/v1/dimona/entrees"));
        foreach (var niss in new[] { nissSalarie, nissEtudiant, nissNonAffilie })
        {
            (await OccurrencesEnBase(connexion, niss)).ShouldBe(0);
            fixture.Journal.Entrees.Where(e => e.Contains(niss)).ShouldBeEmpty();
        }

        var publisher = (InMemoryMessagePublisher)fixture.Factory.Services.GetRequiredService<IMessagePublisher>();
        publisher.Published.ShouldAllBe(m => !m.Payload.Contains(nissSalarie) && !m.Payload.Contains(nissEtudiant));

        // 7. Purge (conservation après traitement : 0 en test) : seules les charges des échanges traités disparaissent.
        await fixture.Factory.Services.GetRequiredService<PlanificateurFlux>().ExecuterUnPassageAsync([], _ct);
        await using (var commande = new NpgsqlCommand(
                         "SELECT statut, count(*) FILTER (WHERE charge_utile_chiffree IS NOT NULL) FROM journal_flux WHERE flux = 'Dimona' GROUP BY statut ORDER BY statut", connexion))
        {
            await using var lecteur = await commande.ExecuteReaderAsync(_ct);
            var restantes = new Dictionary<string, long>();
            while (await lecteur.ReadAsync(_ct))
            {
                restantes[lecteur.GetString(0)] = lecteur.GetInt64(1);
            }

            restantes.ShouldBe(new Dictionary<string, long> { ["Rejete"] = 1, ["Traite"] = 0 });
        }

        var purge = await Gestionnaire.GetFromJsonAsync<EchangeFluxDto>($"/api/v1/flux/journal/{enErreur[0].Id}", Json, _ct);
        purge!.ChargeUtileDisponible.ShouldBeFalse();
        purge.ChargeUtilePurgeeLe.ShouldNotBeNull();
    }

    [Fact]
    public async Task Une_mutation_du_registre_national_reste_en_erreur_tant_que_personnes_n_expose_pas_l_api()
    {
        var rapport = await Lancer("registre-national");

        rapport.Recus.ShouldBe(1);
        rapport.EnErreur.ShouldBe(1);
        var echange = (await Journal("?flux=registre-national")).Elements.ShouldHaveSingleItem();
        echange.CodeErreur.ShouldBe("integrations.personnes-api-mutation-absente");
        echange.ReferenceExterne.ShouldBe("SIM-RN-0001");
        echange.ChargeUtileDisponible.ShouldBeTrue();

        await using var connexion = new NpgsqlConnection(fixture.ConnectionString);
        await connexion.OpenAsync(_ct);
        (await OccurrencesEnBase(connexion, SimulateurRegistreNational.NissMutation)).ShouldBe(0);
        fixture.Journal.Entrees.Where(e => e.Contains(SimulateurRegistreNational.NissMutation)).ShouldBeEmpty();
    }

    private async Task<RapportExecutionDto> Lancer(string flux)
    {
        var reponse = await Gestionnaire.PostAsync($"/api/v1/flux/{flux}/executions", null, _ct);
        reponse.StatusCode.ShouldBe(HttpStatusCode.OK, await reponse.Content.ReadAsStringAsync(_ct));
        return (await reponse.Content.ReadFromJsonAsync<RapportExecutionDto>(Json, _ct))!;
    }

    private async Task<PageDto<EchangeFluxDto>> Journal(string filtre) =>
        (await Gestionnaire.GetFromJsonAsync<PageDto<EchangeFluxDto>>($"/api/v1/flux/journal{filtre}", Json, _ct))!;

    private async Task<int> Dispatcher<TEvent>(Guid messageId, TEvent integrationEvent)
        where TEvent : IntegrationEvent
    {
        var dispatcher = fixture.Factory.Services.GetRequiredService<IntegrationEventDispatcher<IntegrationsDbContext>>();
        return await dispatcher.DispatchAsync(messageId, EventContractAttribute.Of(typeof(TEvent)).FullName,
            JsonSerializer.Serialize(integrationEvent, EventSerialization.Options), _ct);
    }

    internal static async Task<long> OccurrencesEnBase(NpgsqlConnection connexion, string valeur)
    {
        // Liste fermée des tables : un test échoue si une nouvelle table n'est pas ajoutée à la vérification.
        await using (var tables = new NpgsqlCommand(
                         "SELECT string_agg(table_name, ',' ORDER BY table_name) FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'",
                         connexion))
        {
            ((string)(await tables.ExecuteScalarAsync())!).ShouldBe(
                "__EFMigrationsHistory,correspondance_identifiant,entreprise_bce,inbox_message,journal_flux,outbox_message,position_flux,unite_etablissement_bce");
        }

        await using var commande = new NpgsqlCommand(
            """
            SELECT (SELECT count(*) FROM correspondance_identifiant t WHERE t::text LIKE @motif)
                 + (SELECT count(*) FROM entreprise_bce t WHERE t::text LIKE @motif)
                 + (SELECT count(*) FROM inbox_message t WHERE t::text LIKE @motif)
                 + (SELECT count(*) FROM journal_flux t WHERE t::text LIKE @motif)
                 + (SELECT count(*) FROM outbox_message t WHERE t::text LIKE @motif)
                 + (SELECT count(*) FROM position_flux t WHERE t::text LIKE @motif)
                 + (SELECT count(*) FROM unite_etablissement_bce t WHERE t::text LIKE @motif)
            """,
            connexion);
        commande.Parameters.AddWithValue("motif", $"%{valeur}%");
        return (long)(await commande.ExecuteScalarAsync())!;
    }
}
