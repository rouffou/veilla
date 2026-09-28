using Sepp.BuildingBlocks.Domain;
using Sepp.Referentiels.Domain.Parametres;
using Shouldly;

namespace Sepp.Referentiels.Domain.Tests;

public class ParametreLegalTests
{
    private static readonly LocalizedLabel Libelle = new("Délai", "Termijn", "Frist");

    private static ParametreLegal DelaiReprise() =>
        ParametreLegal.Creer(new CodeParametre("SANTE.REPRISE.DELAI"), Libelle, UniteParametre.JoursOuvrables, "Code", 10, new DateOnly(2026, 1, 1));

    [Fact]
    public void La_valeur_est_celle_en_vigueur_a_la_date()
    {
        var parametre = DelaiReprise();
        parametre.DefinirValeur(8, new DateOnly(2027, 1, 1));

        parametre.ValeurAu(new DateOnly(2025, 12, 31)).ShouldBeNull();
        parametre.ValeurAu(new DateOnly(2026, 6, 1)).ShouldBe(10);
        parametre.ValeurAu(new DateOnly(2026, 12, 31)).ShouldBe(10);
        parametre.ValeurAu(new DateOnly(2027, 1, 1)).ShouldBe(8);
    }

    [Fact]
    public void Une_nouvelle_valeur_cloture_la_precedente_au_lieu_de_la_modifier()
    {
        var parametre = DelaiReprise();
        parametre.DefinirValeur(8, new DateOnly(2027, 1, 1));

        parametre.Valeurs.Count.ShouldBe(2);
        parametre.Valeurs.Single(v => v.Valeur == 10).Validite.ValidTo.ShouldBe(new DateOnly(2027, 1, 1));
        parametre.Valeurs.Single(v => v.Valeur == 8).Validite.IsOpen.ShouldBeTrue();
        parametre.DomainEvents.Count.ShouldBe(2);
    }

    [Fact]
    public void Une_valeur_ne_peut_pas_etre_antidatee() =>
        Should.Throw<DomainException>(() => DelaiReprise().DefinirValeur(8, new DateOnly(2026, 1, 1)));

    [Fact]
    public void Un_delai_en_jours_doit_etre_entier() =>
        Should.Throw<DomainException>(() => DelaiReprise().DefinirValeur(7.5m, new DateOnly(2027, 1, 1)));

    [Fact]
    public void Une_valeur_negative_est_refusee() =>
        Should.Throw<DomainException>(() => DelaiReprise().DefinirValeur(-1, new DateOnly(2027, 1, 1)));

    [Theory]
    [InlineData("sante.reprise")]
    [InlineData("SANTE REPRISE")]
    [InlineData("")]
    public void Un_code_invalide_est_refuse(string code) =>
        Should.Throw<DomainException>(() => new CodeParametre(code));
}
