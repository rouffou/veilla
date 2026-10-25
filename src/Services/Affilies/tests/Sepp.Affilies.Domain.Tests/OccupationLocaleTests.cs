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

    [Fact]
    public void Un_instantane_remplit_une_occupation_inconnue()
    {
        var occupation = Occupation();

        occupation.AppliquerInstantane(Jour, Jour.AddDays(30), DateTimeOffset.UtcNow).ShouldBeTrue();

        occupation.DateDebut.ShouldBe(Jour);
        occupation.DateFin.ShouldBe(Jour.AddDays(30));
        occupation.EstActiveAu(Jour.AddDays(31)).ShouldBeFalse();
    }

    [Fact]
    public void Un_instantane_rejoue_ne_change_rien()
    {
        var occupation = Occupation();
        var etatDu = DateTimeOffset.UtcNow;
        occupation.AppliquerInstantane(Jour, null, etatDu);

        occupation.AppliquerInstantane(Jour, null, etatDu).ShouldBeFalse();
    }

    [Fact]
    public void Un_instantane_plus_ancien_qu_une_fin_recue_ne_l_ecrase_pas()
    {
        var occupation = Occupation();
        var maintenant = DateTimeOffset.UtcNow;
        occupation.Terminer(Jour.AddDays(10), maintenant);

        // L'instantané date d'avant la fin : seul le début manquant est complété.
        occupation.AppliquerInstantane(Jour, null, maintenant.AddMinutes(-5)).ShouldBeTrue();
        occupation.DateDebut.ShouldBe(Jour);
        occupation.DateFin.ShouldBe(Jour.AddDays(10));

        occupation.AppliquerInstantane(Jour.AddDays(-3), null, maintenant.AddMinutes(-5)).ShouldBeFalse();
        occupation.DateDebut.ShouldBe(Jour);
    }

    [Fact]
    public void Un_instantane_plus_recent_remplace_l_etat_connu_et_protege_contre_une_fin_perimee()
    {
        var occupation = Occupation();
        var maintenant = DateTimeOffset.UtcNow;
        occupation.Debuter(Jour);
        occupation.Terminer(Jour.AddDays(10), maintenant);

        // La fin a été annulée chez Personnes après coup : l'instantané le reflète.
        occupation.AppliquerInstantane(Jour, null, maintenant.AddMinutes(5)).ShouldBeTrue();
        occupation.DateFin.ShouldBeNull();

        occupation.Terminer(Jour.AddDays(10), maintenant).ShouldBeFalse();
        occupation.DateFin.ShouldBeNull();
    }
}
