using Sepp.BuildingBlocks.Domain.Calendar;

using Shouldly;

namespace Sepp.BuildingBlocks.Tests;

public class BusinessCalendarTests
{
    private static readonly BusinessCalendar Calendar = BusinessCalendar.Belgian(2025, 2026, 2027);

    [Fact]
    public void Retrancher_zero_jour_retourne_la_date_de_depart_meme_non_ouvrable()
    {
        var samedi = new DateOnly(2026, 9, 26);

        Calendar.SubtractBusinessDays(samedi, 0).ShouldBe(samedi);
    }

    [Fact]
    public void Retrancher_un_jour_depuis_un_lundi_donne_le_vendredi_precedent() =>
        Calendar.SubtractBusinessDays(new DateOnly(2026, 9, 28), 1).ShouldBe(new DateOnly(2026, 9, 25));

    [Fact]
    public void Les_week_ends_ne_comptent_pas() =>
        Calendar.SubtractBusinessDays(new DateOnly(2026, 9, 28), 5).ShouldBe(new DateOnly(2026, 9, 21));

    [Fact]
    public void Retrancher_depuis_un_samedi_compte_a_partir_du_vendredi() =>
        Calendar.SubtractBusinessDays(new DateOnly(2026, 9, 26), 1).ShouldBe(new DateOnly(2026, 9, 25));

    [Fact]
    public void Les_jours_feries_ne_comptent_pas()
    {
        // Le 11 novembre 2026 (mercredi) est férié : 3 jours ouvrables avant le jeudi 12 novembre = vendredi 6 novembre.
        Calendar.IsHoliday(new DateOnly(2026, 11, 11)).ShouldBeTrue();
        Calendar.SubtractBusinessDays(new DateOnly(2026, 11, 12), 2).ShouldBe(new DateOnly(2026, 11, 9));
        Calendar.SubtractBusinessDays(new DateOnly(2026, 11, 12), 3).ShouldBe(new DateOnly(2026, 11, 6));
    }

    [Fact]
    public void Un_jour_de_remplacement_supplementaire_est_exclu()
    {
        var calendar = new BusinessCalendar([new DateOnly(2026, 9, 24)]);

        calendar.SubtractBusinessDays(new DateOnly(2026, 9, 25), 1).ShouldBe(new DateOnly(2026, 9, 23));
    }

    [Fact]
    public void Le_passage_d_annee_est_gere()
    {
        // 1er janvier 2026 (jeudi) férié, 2 janvier vendredi : 2 jours ouvrables avant le lundi 5 janvier = mercredi 31 décembre.
        Calendar.SubtractBusinessDays(new DateOnly(2026, 1, 5), 1).ShouldBe(new DateOnly(2026, 1, 2));
        Calendar.SubtractBusinessDays(new DateOnly(2026, 1, 5), 2).ShouldBe(new DateOnly(2025, 12, 31));
    }

    [Theory]
    [InlineData(2026, 9, 28, 10)]
    [InlineData(2026, 12, 21, 7)]
    [InlineData(2026, 1, 5, 3)]
    public void Retrancher_est_l_inverse_d_ajouter_a_partir_d_un_jour_ouvrable(int year, int month, int day, int days)
    {
        var start = new DateOnly(year, month, day);
        Calendar.IsBusinessDay(start).ShouldBeTrue();

        var avant = Calendar.SubtractBusinessDays(start, days);

        Calendar.AddBusinessDays(avant, days).ShouldBe(start);
    }

    [Fact]
    public void Un_nombre_negatif_de_jours_est_refuse() =>
        Should.Throw<ArgumentOutOfRangeException>(() => Calendar.SubtractBusinessDays(new DateOnly(2026, 9, 28), -1));
}
