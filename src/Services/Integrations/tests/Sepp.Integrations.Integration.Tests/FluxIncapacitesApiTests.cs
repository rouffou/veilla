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
using Sepp.Integrations.Application.Externe;
using Sepp.Integrations.Application.Flux;
using Sepp.Integrations.Domain;
using Sepp.Integrations.Domain.Flux;

using Shouldly;

namespace Sepp.Integrations.Integration.Tests;

/// <summary>
/// Flux eHealth des notifications d'incapacité de bout en bout (SAN-60) : simulateur, journal, identification par le
/// service Personnes (simulé), publication de integrations.incapacite-notifiee sans NISS. Fixture dédiée : les simulateurs
/// servent les premiers affiliés connus.
/// </summary>
public sealed class FluxIncapacitesApiTests(IntegrationsApiFixture fixture) : IClassFixture<IntegrationsApiFixture>
{
    private static readonly JsonSerializerOptions Json = FluxDimonaApiTests.Json;

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private HttpClient Gestionnaire => fixture.Client(Roles.GestionnaireDossiers);

    [Fact]
    public async Task Le_flux_eHealth_des_incapacites_publie_incapacite_notifiee_sans_niss_ni_en_base_ni_dans_l_evenement()
    {
        // SAN-60 : employeur connu (correspondance), salarié et étudiant simulés connus de Personnes.
        var employeur = NumerosBce.AvecControle("00000001");
        var affilie = Guid.CreateVersion7();
        (await Dispatcher(Guid.CreateVersion7(), new AffilieCree(affilie, employeur, "A"))).ShouldBe(1);
        var notifications = SimulateurIncapacites.Notifications(employeur);
        fixture.Personnes.ConnaitPersonne(notifications[0].Niss);
        fixture.Personnes.ConnaitPersonne(notifications[2].Niss);

        var rapport = await Lancer("incapacites");

        rapport.Flux.ShouldBe(TypeFlux.Incapacites);
        var journal = await Journal("?flux=incapacites&taille=200");
        journal.Elements.Count(e => e.Statut == StatutEchange.Traite && e.ReferenceExterne!.StartsWith($"SIMINC{employeur}", StringComparison.Ordinal)).ShouldBe(3);
        journal.Elements.ShouldContain(e => e.CodeErreur == "integrations.personne-inconnue");
        journal.Elements.ShouldContain(e => e.CodeErreur == "integrations.employeur-inconnu");

        await using var connexion = new NpgsqlConnection(fixture.ConnectionString);
        await connexion.OpenAsync(_ct);
        await using var commande = new NpgsqlCommand(
            "SELECT payload::text FROM outbox_message WHERE event_type = 'integrations.incapacite-notifiee.v1' AND payload->>'affilieId' = @a", connexion);
        commande.Parameters.AddWithValue("a", affilie.ToString());
        var charges = new List<string>();
        await using (var lecteur = await commande.ExecuteReaderAsync(_ct))
        {
            while (await lecteur.ReadAsync(_ct))
            {
                charges.Add(lecteur.GetString(0));
            }
        }

        charges.Count.ShouldBe(3);
        charges.ShouldAllBe(c => !c.Contains(notifications[0].Niss) && !c.Contains(notifications[2].Niss));
        charges.Count(c => c.Contains(fixture.Personnes.PersonneDe(notifications[0].Niss).ToString())).ShouldBe(2);
        charges.ShouldContain(c => c.Contains("MEDECIN_CONSEIL"));
        fixture.Journal.Entrees.ShouldNotContain(e => e.Contains(notifications[0].Niss));

        // Une seconde exécution ne relit rien (position du flux) ni ne republie.
        (await Lancer("incapacites")).Recus.ShouldBe(0);
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
}
