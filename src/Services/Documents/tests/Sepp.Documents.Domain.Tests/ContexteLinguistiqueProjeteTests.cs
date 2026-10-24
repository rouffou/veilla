using Sepp.BuildingBlocks.Domain;
using Sepp.Documents.Domain.Langues;

using Shouldly;

namespace Sepp.Documents.Domain.Tests;

public sealed class ContexteLinguistiqueProjeteTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Un_etat_plus_ancien_est_refuse_sans_rien_modifier()
    {
        var contexte = ContexteLinguistiqueProjete.Affilie(Guid.CreateVersion7(), RegimeLinguistique.Neerlandais, Language.Nl, T0);

        contexte.Appliquer(RegimeLinguistique.Francais, Language.Fr, T0.AddSeconds(-1)).ShouldBeFalse();

        contexte.Regime.ShouldBe(RegimeLinguistique.Neerlandais);
        contexte.Langue.ShouldBe(Language.Nl);
        contexte.EvenementDu.ShouldBe(T0);
    }

    [Fact]
    public void A_horodatage_egal_le_dernier_recu_l_emporte()
    {
        var contexte = ContexteLinguistiqueProjete.Personne(Guid.CreateVersion7(), Language.Fr, T0);

        contexte.Appliquer(null, Language.De, T0).ShouldBeTrue();

        contexte.Langue.ShouldBe(Language.De);
    }

    [Fact]
    public void Une_valeur_absente_ne_remplace_pas_la_valeur_connue()
    {
        var contexte = ContexteLinguistiqueProjete.Affilie(Guid.CreateVersion7(), RegimeLinguistique.Allemand, Language.De, T0);

        contexte.Appliquer(null, Language.Fr, T0.AddMinutes(1)).ShouldBeTrue();

        contexte.Regime.ShouldBe(RegimeLinguistique.Allemand);
        contexte.Langue.ShouldBe(Language.Fr);
        contexte.EvenementDu.ShouldBe(T0.AddMinutes(1));
    }

    [Fact]
    public void Une_personne_n_a_jamais_de_regime_linguistique()
    {
        var contexte = ContexteLinguistiqueProjete.Personne(Guid.CreateVersion7(), Language.Nl, T0);

        contexte.Appliquer(RegimeLinguistique.Francais, null, T0.AddMinutes(1));

        contexte.Regime.ShouldBeNull();
        contexte.Langue.ShouldBe(Language.Nl);
    }
}
