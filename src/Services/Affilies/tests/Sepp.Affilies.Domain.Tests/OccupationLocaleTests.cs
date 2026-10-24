using Sepp.Affilies.Domain.Occupations;

using Shouldly;

namespace Sepp.Affilies.Domain.Tests;

/// <summary>POR-11, #340 : projection d'une occupation, complétée dans n'importe quel ordre par ses deux événements.</summary>
public class OccupationLocaleTests
{
    private static readonly DateOnly Jour = new(2026, 10, 1);

    private static OccupationLocale Occupation() => new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

    [Fact]
    public void Une_occupation_dont_le_debut_n_est_pas_connu_n_est_pas_active()
    {
        var occupation = Occupation();

        occupation.EstActiveAu(Jour).ShouldBeFalse();
    }

    [Fact]
    public void Une_occupation_commencee_sans_fin_est_active_a_partir_de_son_debut()
    {
        var occupation = Occupation();
        occupation.Debuter(Jour);

        occupation.EstActiveAu(Jour.AddDays(-1)).ShouldBeFalse();
        occupation.EstActiveAu(Jour).ShouldBeTrue();
        occupation.EstActiveAu(Jour.AddYears(3)).ShouldBeTrue();
    }

    [Fact]
    public void Le_dernier_jour_d_occupation_est_inclus()
    {
        var occupation = Occupation();
        occupation.Debuter(Jour);
        occupation.Terminer(Jour.AddDays(10), DateTimeOffset.UtcNow).ShouldBeTrue();

        occupation.EstActiveAu(Jour.AddDays(10)).ShouldBeTrue();
        occupation.EstActiveAu(Jour.AddDays(11)).ShouldBeFalse();
    }

    [Fact]
    public void Une_fin_recue_avant_le_debut_complete_la_meme_occupation()
    {
        var occupation = Occupation();
        occupation.Terminer(Jour.AddDays(10), DateTimeOffset.UtcNow);
        occupation.EstActiveAu(Jour).ShouldBeFalse();

        occupation.Debuter(Jour);

        occupation.EstActiveAu(Jour.AddDays(5)).ShouldBeTrue();
        occupation.EstActiveAu(Jour.AddDays(11)).ShouldBeFalse();
    }

    [Fact]
    public void Une_fin_plus_ancienne_que_celle_deja_appliquee_est_ignoree()
    {
        var occupation = Occupation();
        var maintenant = DateTimeOffset.UtcNow;
        occupation.Terminer(Jour.AddDays(20), maintenant).ShouldBeTrue();

        occupation.Terminer(Jour.AddDays(5), maintenant.AddMinutes(-1)).ShouldBeFalse();

        occupation.DateFin.ShouldBe(Jour.AddDays(20));
    }
}
