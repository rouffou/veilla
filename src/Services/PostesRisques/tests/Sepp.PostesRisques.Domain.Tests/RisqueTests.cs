using Sepp.BuildingBlocks.Domain;
using Sepp.PostesRisques.Domain.Risques;

using Shouldly;

namespace Sepp.PostesRisques.Domain.Tests;

public class RisqueTests
{
    private static readonly DateOnly Debut = new(2026, 1, 1);

    internal static Risque Bruit()
    {
        var risque = Risque.Creer(" ex.phys.bruit ", CategorieRisque.Physique,
            new LocalizedLabel("Bruit (exemple)", "Lawaai (voorbeeld)", "Lärm (Beispiel)"), "Exemple — à remplacer");
        risque.DefinirRegle(TypeSurveillance.EvaluationSantePeriodique, 36, ["audiometrie"], [], false, Debut);
        return risque;
    }

    [Fact]
    public void Le_code_est_normalise_et_la_premiere_regle_porte_la_version_1()
    {
        var risque = Bruit();

        risque.Code.ShouldBe("EX.PHYS.BRUIT");
        var regle = risque.RegleAu(Debut).ShouldNotBeNull();
        regle.Version.ShouldBe(1);
        regle.ActesSupplementaires.ShouldBe(["AUDIOMETRIE"]);
    }

    [Fact]
    public void Une_nouvelle_regle_cloture_la_precedente_et_incremente_la_version()
    {
        var risque = Bruit();

        var nouvelle = risque.DefinirRegle(TypeSurveillance.EvaluationSantePeriodique, 24, ["AUDIOMETRIE"], [], true, new DateOnly(2027, 1, 1));

        nouvelle!.Version.ShouldBe(2);
        risque.RegleAu(new DateOnly(2026, 12, 31))!.FrequenceMois.ShouldBe(36);
        risque.RegleAu(new DateOnly(2027, 1, 1))!.FrequenceMois.ShouldBe(24);
        risque.Regles.Count.ShouldBe(2);
    }

    [Fact]
    public void Une_regle_identique_ne_cree_pas_de_version() =>
        Bruit().DefinirRegle(TypeSurveillance.EvaluationSantePeriodique, 36, ["AUDIOMETRIE"], [], false, new DateOnly(2027, 1, 1)).ShouldBeNull();

    [Fact]
    public void Une_regle_antidatee_est_refusee() =>
        Should.Throw<DomainException>(() => Bruit().DefinirRegle(TypeSurveillance.EvaluationSantePeriodique, 12, [], [], false, Debut));

    [Fact]
    public void Une_surveillance_periodique_exige_une_frequence() =>
        Should.Throw<DomainException>(() => Bruit().DefinirRegle(TypeSurveillance.EvaluationSantePeriodique, null, [], [], false, new DateOnly(2027, 1, 1)));

    [Fact]
    public void Une_evaluation_prealable_uniquement_n_a_pas_de_frequence() =>
        Should.Throw<DomainException>(() => Bruit().DefinirRegle(TypeSurveillance.EvaluationPrealableUniquement, 12, [], [], false, new DateOnly(2027, 1, 1)));

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public void Une_frequence_hors_bornes_est_refusee(int frequence) =>
        Should.Throw<DomainException>(() => Bruit().DefinirRegle(TypeSurveillance.ActesMedicauxSupplementaires, frequence, [], [], false, new DateOnly(2027, 1, 1)));

    [Fact]
    public void Mettre_a_jour_sans_changement_ne_fait_rien()
    {
        var risque = Bruit();

        risque.MettreAJour(CategorieRisque.Physique, new LocalizedLabel("Bruit (exemple)", "Lawaai (voorbeeld)", "Lärm (Beispiel)"), "Exemple — à remplacer")
            .ShouldBeFalse();
        risque.MettreAJour(CategorieRisque.Physique, new LocalizedLabel("Bruit", "Lawaai", "Lärm"), "Exemple — à remplacer").ShouldBeTrue();
    }
}
