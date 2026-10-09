using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.Contracts.Integrations;
using Sepp.Integrations.Application.Correspondances;
using Sepp.Integrations.Application.Flux;
using Sepp.Integrations.Domain;
using Sepp.Integrations.Domain.Flux;

using Shouldly;

namespace Sepp.Integrations.Integration.Tests;

/// <summary>Migrations, API de suivi (INT-02), flux BCE, correspondances et autorisations contre un vrai PostgreSQL.</summary>
public sealed class IntegrationsApiTests(IntegrationsApiFixture fixture) : IClassFixture<IntegrationsApiFixture>
{
    private static readonly JsonSerializerOptions Json = FluxDimonaApiTests.Json;

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private HttpClient Gestionnaire => fixture.Client(Roles.GestionnaireDossiers);

    [Fact]
    public async Task Les_sondes_repondent_et_le_suivi_des_flux_est_reserve()
    {
        var anonyme = fixture.Factory.CreateClient();
        (await anonyme.GetAsync("/health/ready", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonyme.GetAsync("/api/v1/flux/journal", _ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        foreach (var role in new[] { Roles.Cpmt, Roles.Employeur, Roles.Integrations })
        {
            (await fixture.Client(role).GetAsync("/api/v1/flux/volumes", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await fixture.Client(role).PostAsync("/api/v1/flux/dimona/executions", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        (await fixture.Client(Roles.AdministrateurFonctionnel).GetAsync("/api/v1/flux/volumes", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Gestionnaire.GetAsync("/api/v1/flux/journal?flux=inconnu", _ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Gestionnaire.PostAsync("/api/v1/flux/inconnu/executions", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Gestionnaire.GetAsync($"/api/v1/flux/journal/{Guid.CreateVersion7()}", _ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Une_consultation_bce_publie_les_donnees_et_expose_les_unites_d_etablissement()
    {
        var numero = NumerosBce.AvecControle("05000001");
        var affilie = Guid.CreateVersion7();
        var correspondance = await Gestionnaire.PutAsJsonAsync("/api/v1/correspondances",
            new { typeExterne = "NumeroBce", valeurExterne = NumerosBce.Formater(numero), typeInterne = "Affilie", identifiantInterne = affilie }, _ct);
        correspondance.StatusCode.ShouldBe(HttpStatusCode.OK, await correspondance.Content.ReadAsStringAsync(_ct));

        var reponse = await Gestionnaire.PostAsJsonAsync("/api/v1/flux/bce/consultations", new { numeroBce = numero }, _ct);
        reponse.StatusCode.ShouldBe(HttpStatusCode.OK, await reponse.Content.ReadAsStringAsync(_ct));
        var echange = (await reponse.Content.ReadFromJsonAsync<EchangeFluxDto>(Json, _ct))!;
        echange.Statut.ShouldBe(StatutEchange.Traite);
        echange.Flux.ShouldBe(TypeFlux.Bce);
        echange.ReferenceExterne.ShouldBe(numero);

        // Même consultation le même jour : l'échange existant est renvoyé (idempotence de la réception).
        var rejeu = await (await Gestionnaire.PostAsJsonAsync("/api/v1/flux/bce/consultations", new { numeroBce = numero }, _ct))
            .Content.ReadFromJsonAsync<EchangeFluxDto>(Json, _ct);
        rejeu!.Id.ShouldBe(echange.Id);
        var relance = await (await Gestionnaire.PostAsync($"/api/v1/flux/journal/{echange.Id}/relance", null, _ct)).Content.ReadFromJsonAsync<EchangeFluxDto>(Json, _ct);
        relance!.Tentatives.ShouldBe(1);

        var publisher = (InMemoryMessagePublisher)fixture.Factory.Services.GetRequiredService<IMessagePublisher>();
        await Eventually(() => publisher.Published.Any(m => m.EventType == "integrations.donnees-bce-recues.v1" && m.Payload.Contains(numero)));
        var evenement = JsonSerializer.Deserialize<DonneesBceRecues>(
            publisher.Published.Single(m => m.EventType == "integrations.donnees-bce-recues.v1" && m.Payload.Contains(numero)).Payload, EventSerialization.Options)!;
        evenement.AffilieId.ShouldBe(affilie);
        evenement.Denomination.ShouldBe($"Entreprise simulée {NumerosBce.Formater(numero)}");
        evenement.NumerosUnitesEtablissement.ShouldNotBeEmpty();

        // Les adresses ne voyagent pas dans l'événement : elles se lisent par l'API (profil qui lit les affiliés).
        var entreprise = await fixture.Client(Roles.Employeur).GetFromJsonAsync<EntrepriseBceDto>($"/api/v1/bce/entreprises/{numero}", Json, _ct);
        entreprise!.AffilieId.ShouldBe(affilie);
        entreprise.UnitesEtablissement.Select(u => u.Numero).ShouldBe(evenement.NumerosUnitesEtablissement);
        entreprise.UnitesEtablissement.ShouldAllBe(u => u.Adresse.Rue == "Rue de la Simulation");
        (await fixture.Client(Roles.Integrations).GetAsync($"/api/v1/bce/entreprises/{numero}", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        // Le compte technique du service Affiliés (consommateur de donnees-bce-recues) lit ces données publiques, rien de plus.
        (await fixture.Client(Roles.Affilies).GetFromJsonAsync<EntrepriseBceDto>($"/api/v1/bce/entreprises/{numero}", Json, _ct))!.AffilieId.ShouldBe(affilie);
        (await fixture.Client(Roles.Affilies).GetAsync("/api/v1/flux/journal", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Gestionnaire.GetAsync($"/api/v1/bce/entreprises/{NumerosBce.AvecControle("05000002")}", _ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Une_entreprise_inconnue_ou_un_numero_invalide_est_refuse()
    {
        (await Gestionnaire.PostAsJsonAsync("/api/v1/flux/bce/consultations", new { numeroBce = NumerosBce.AvecControle("05000999") }, _ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Gestionnaire.PostAsJsonAsync("/api/v1/flux/bce/consultations", new { numeroBce = "0202239952" }, _ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Gestionnaire.PutAsJsonAsync("/api/v1/correspondances",
                new { typeExterne = "NumeroBce", valeurExterne = "0202239952", typeInterne = "Affilie", identifiantInterne = Guid.CreateVersion7() }, _ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Le_tableau_de_suivi_donne_les_volumes_par_flux_et_par_jour()
    {
        var numero = NumerosBce.AvecControle("05000003");
        (await Gestionnaire.PostAsJsonAsync("/api/v1/flux/bce/consultations", new { numeroBce = numero }, _ct)).EnsureSuccessStatusCode();

        var volumes = await Gestionnaire.GetFromJsonAsync<VolumesDto>("/api/v1/flux/volumes", Json, _ct);
        var bce = volumes!.Totaux.Single(v => v.Flux == TypeFlux.Bce);
        bce.Echanges.ShouldBeGreaterThanOrEqualTo(1);
        bce.Traites.ShouldBeGreaterThanOrEqualTo(1);
        volumes.ParJour.ShouldContain(v => v.Flux == TypeFlux.Bce && v.Jour == volumes.Au);

        var journal = await Gestionnaire.GetFromJsonAsync<PageDto<EchangeFluxDto>>($"/api/v1/flux/journal?flux=bce&statut=Traite&statut=Rejete&du={volumes.Au:yyyy-MM-dd}&au={volumes.Au:yyyy-MM-dd}", Json, _ct);
        journal!.Elements.ShouldContain(e => e.ReferenceExterne == numero);
        (await Gestionnaire.GetAsync("/api/v1/flux/volumes?du=2026-01-01&au=2027-06-01", _ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Les_correspondances_se_consultent_par_valeur_ou_par_objet_interne()
    {
        var numero = NumerosBce.AvecControle("05000004");
        var affilie = Guid.CreateVersion7();
        (await Gestionnaire.PutAsJsonAsync("/api/v1/correspondances",
            new { typeExterne = "NumeroBce", valeurExterne = numero, typeInterne = "Affilie", identifiantInterne = affilie }, _ct)).EnsureSuccessStatusCode();
        (await Gestionnaire.PutAsJsonAsync("/api/v1/correspondances",
            new { typeExterne = "NumeroBce", valeurExterne = numero, typeInterne = "Affilie", identifiantInterne = affilie }, _ct)).EnsureSuccessStatusCode();

        var parValeur = await Gestionnaire.GetFromJsonAsync<List<CorrespondanceDto>>($"/api/v1/correspondances?type=NumeroBce&valeur={NumerosBce.Formater(numero)}", Json, _ct);
        var parObjet = await Gestionnaire.GetFromJsonAsync<List<CorrespondanceDto>>($"/api/v1/correspondances?identifiantInterne={affilie}", Json, _ct);

        parValeur!.ShouldHaveSingleItem().IdentifiantInterne.ShouldBe(affilie);
        parObjet!.ShouldHaveSingleItem().ValeurExterne.ShouldBe(numero);
        (await fixture.Client(Roles.Employeur).GetAsync("/api/v1/correspondances", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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
