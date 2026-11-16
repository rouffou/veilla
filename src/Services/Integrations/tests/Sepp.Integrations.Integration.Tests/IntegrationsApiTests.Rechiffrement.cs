using System.Net;
using System.Net.Http.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.Integrations.Adapters.Persistence;
using Sepp.Integrations.Domain;

using Shouldly;

namespace Sepp.Integrations.Integration.Tests;

/// <summary>
/// Rechiffrement des champs (#396, ADR 0012) sur la base réelle du service (charges utiles des flux) : toutes les colonnes chiffrées du
/// modèle sont couvertes (garde-fou : une colonne ajoutée ou renommée doit apparaître ici), et une base dont toutes les valeurs
/// utilisent déjà la DEK courante n'est pas réécrite.
/// </summary>
public sealed partial class IntegrationsApiTests
{
    /// <summary>
    /// Garde-fou d'architecture et de données (#482) : tout convertisseur qui chiffre porte le marqueur et le suffixe
    /// (sinon <see cref="ColonnesChiffrees.Depuis"/> échoue), et la vérification fondée sur les données ne trouve aucune
    /// valeur chiffrée dans une colonne que le modèle ne déclare pas chiffrée.
    /// </summary>
    [Fact]
    public async Task Les_convertisseurs_de_chiffrement_sont_marques_et_aucune_valeur_chiffree_n_est_hors_du_modele()
    {
        // #502, P3-C : au moins une ligne par table chiffrée (journal des flux, charge utile d'une consultation BCE), pour
        // que la vérification fondée sur les données porte sur des valeurs réelles.
        var consultation = await Gestionnaire.PostAsJsonAsync("/api/v1/flux/bce/consultations", new { numeroBce = NumerosBce.AvecControle("05000502") }, _ct);
        consultation.StatusCode.ShouldBe(HttpStatusCode.OK, await consultation.Content.ReadAsStringAsync(_ct));

        IReadOnlyList<ColonneChiffree> colonnes;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            colonnes = ColonnesChiffrees.Depuis(scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>().Model);
        }

        var verification = await RechiffrementHote.VerifierAsync<IntegrationsDbContext>(
            fixture.Factory.Services, s => s.GetRequiredService<FieldEncryptor>(), new OptionsRechiffrement(), NullLogger.Instance, _ct);

        colonnes.ShouldNotBeEmpty();
        verification.ColonnesHorsModele.ShouldBeEmpty();
        verification.ColonnesAbsentes.ShouldBeEmpty();
        verification.Colonnes.Where(c => c.DuModele).Select(c => c.Colonne).ShouldBe(colonnes.Select(c => c.Nom));
        verification.Colonnes.Where(c => c.DuModele).GroupBy(c => c.Colonne[..c.Colonne.LastIndexOf('.')])
            .Where(t => t.Sum(c => c.Chiffrees) == 0).Select(t => t.Key).ShouldBeEmpty("table chiffrée sans aucune valeur chiffrée");
    }

    [Fact]
    public async Task Le_rechiffrement_couvre_toutes_les_colonnes_chiffrees_et_ne_reecrit_pas_les_valeurs_a_jour()
    {
        var rapport = await RechiffrementHote.ExecuterAsync<IntegrationsDbContext>(
            fixture.Factory.Services, s => s.GetRequiredService<FieldEncryptor>(), new OptionsRechiffrement { PauseEntreLots = TimeSpan.Zero }, NullLogger.Instance, _ct);

        rapport.Colonnes.Select(c => c.Colonne).ShouldBe(
        [
            "journal_flux.charge_utile_chiffree",
        ]);
        rapport.Complet.ShouldBeTrue();
        rapport.Rechiffrees.ShouldBe(0);
        rapport.Colonnes.ShouldAllBe(c => c.Examinees == 0);
    }
}
