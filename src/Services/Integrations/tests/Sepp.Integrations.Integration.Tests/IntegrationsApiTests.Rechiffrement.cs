using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.Integrations.Adapters.Persistence;

using Shouldly;

namespace Sepp.Integrations.Integration.Tests;

/// <summary>
/// Rechiffrement des champs (#396, ADR 0012) sur la base réelle du service (charges utiles des flux) : toutes les colonnes chiffrées du
/// modèle sont couvertes (garde-fou : une colonne ajoutée ou renommée doit apparaître ici), et une base dont toutes les valeurs
/// utilisent déjà la DEK courante n'est pas réécrite.
/// </summary>
public sealed partial class IntegrationsApiTests
{
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
