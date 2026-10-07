using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Domain.Calendar;
using Sepp.Planification.Domain.Agenda;

using Shouldly;

namespace Sepp.Planification.Domain.Tests;

/// <summary>PLA-06 : échéances légales en jours ouvrables belges, jours fériés compris (DAT-08).</summary>
public class CalendrierEtDelaisTests
{
    private static readonly BusinessCalendar Calendrier = BusinessCalendar.Belgian(2026, 2027);

    [Fact]
    public void Dix_jours_ouvrables_sautent_noel_et_le_nouvel_an()
    {
        // Départ le vendredi 18 décembre 2026 : sans jour férié l'échéance serait le 31 décembre.
        DelaiLegal.Echeance(new DateOnly(2026, 12, 18), 10, Calendrier).ShouldBe(new DateOnly(2027, 1, 5));
    }

    [Fact]
    public void Les_week_ends_ne_comptent_pas()
    {
        // Départ le vendredi 12 mars 2027 : 10 jours ouvrables = vendredi 26 mars.
        DelaiLegal.Echeance(new DateOnly(2027, 3, 12), 10, Calendrier).ShouldBe(new DateOnly(2027, 3, 26));
    }

    [Fact]
    public void Un_jour_ferie_mobile_repousse_l_echeance()
    {
        // Le lundi de Pâques 2027 tombe le 29 mars : départ le jeudi 25 mars, 3 jours ouvrables → mercredi 31 mars (et non mardi 30).
        DelaiLegal.Echeance(new DateOnly(2027, 3, 25), 3, Calendrier).ShouldBe(new DateOnly(2027, 3, 31));
    }

    [Fact]
    public void Un_rendez_vous_le_jour_de_l_echeance_respecte_le_delai()
    {
        var echeance = new DateOnly(2027, 1, 5);

        DelaiLegal.RespecteEcheance(HeureBelge.VersUtc(echeance, new TimeOnly(23, 30)), echeance).ShouldBeTrue();
        DelaiLegal.RespecteEcheance(HeureBelge.VersUtc(echeance.AddDays(1), new TimeOnly(0, 30)), echeance).ShouldBeFalse();
    }

    [Fact]
    public void L_heure_belge_suit_l_heure_d_ete()
    {
        HeureBelge.VersUtc(new DateOnly(2027, 1, 15), new TimeOnly(9, 0)).Hour.ShouldBe(8);
        HeureBelge.VersUtc(new DateOnly(2027, 7, 15), new TimeOnly(9, 0)).Hour.ShouldBe(7);
    }

    [Fact]
    public void Une_heure_inexistante_au_passage_a_l_heure_d_ete_est_decalee_d_une_heure()
    {
        // 28 mars 2027 : à 02:00 il est 03:00 ; 02:30 n'existe pas.
        HeureBelge.VersUtc(new DateOnly(2027, 3, 28), new TimeOnly(2, 30)).ShouldBe(HeureBelge.VersUtc(new DateOnly(2027, 3, 28), new TimeOnly(3, 30)));
    }

    [Fact]
    public void La_distance_orthodromique_est_plausible()
    {
        var bruxelles = new Coordonnees(50.8503, 4.3517);
        var liege = new Coordonnees(50.6326, 5.5797);

        bruxelles.DistanceKm(liege).ShouldBeInRange(85, 95);
        bruxelles.DistanceKm(bruxelles).ShouldBe(0, 0.001);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(0, 181)]
    [InlineData(double.NaN, 0)]
    public void Des_coordonnees_hors_limites_sont_refusees(double latitude, double longitude) =>
        Should.Throw<DomainException>(() => new Coordonnees(latitude, longitude));
}
