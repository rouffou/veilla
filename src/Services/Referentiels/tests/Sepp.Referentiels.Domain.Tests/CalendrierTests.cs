using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Domain.Calendar;
using Sepp.Referentiels.Domain.Calendrier;

using Shouldly;

namespace Sepp.Referentiels.Domain.Tests;

public class CalendrierTests
{
    [Theory]
    [InlineData(2024, 3, 31)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    public void Paques_est_calculee_correctement(int annee, int mois, int jour) =>
        BelgianPublicHolidays.EasterSunday(annee).ShouldBe(new DateOnly(annee, mois, jour));

    [Fact]
    public void Les_dix_jours_feries_legaux_2026()
    {
        var jours = BelgianPublicHolidays.For(2026).Select(j => j.Date).ToList();

        jours.Count.ShouldBe(10);
        jours.ShouldContain(new DateOnly(2026, 4, 6));   // lundi de Pâques
        jours.ShouldContain(new DateOnly(2026, 5, 14));  // Ascension
        jours.ShouldContain(new DateOnly(2026, 5, 25));  // lundi de Pentecôte
        jours.ShouldContain(new DateOnly(2026, 7, 21));
        jours.ShouldContain(new DateOnly(2026, 11, 11));
    }

    [Fact]
    public void Dix_jours_ouvrables_sautent_week_ends_et_jours_feries()
    {
        var calendrier = BusinessCalendar.Belgian(2026);

        // Reprise le vendredi 1er mai 2026 (férié) : 10 jours ouvrables → lundi 18 mai (Ascension le 14 exclue).
        calendrier.AddBusinessDays(new DateOnly(2026, 5, 1), 10).ShouldBe(new DateOnly(2026, 5, 18));
        calendrier.CountBusinessDays(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 18)).ShouldBe(10);
    }

    [Fact]
    public void Zero_jour_ouvrable_renvoie_la_date_de_depart() =>
        BusinessCalendar.Belgian(2026).AddBusinessDays(new DateOnly(2026, 9, 28), 0).ShouldBe(new DateOnly(2026, 9, 28));

    [Fact]
    public void Un_jour_supplementaire_est_pris_en_compte()
    {
        var calendrier = CalendrierAnnuel.Creer(2026);
        calendrier.AjouterJour(new DateOnly(2026, 9, 28), "FETE_FWB", new LocalizedLabel("Fête de la FWB", "Feest van de FWB", "Fest der FWB"));

        calendrier.JoursFeries().Count.ShouldBe(11);
        calendrier.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<CalendrierModifie>();
    }

    [Fact]
    public void Un_jour_ferie_legal_ne_peut_pas_etre_dedouble()
    {
        var calendrier = CalendrierAnnuel.Creer(2026);

        Should.Throw<DomainException>(() =>
            calendrier.AjouterJour(new DateOnly(2026, 12, 25), "AUTRE", new LocalizedLabel("a", "b", "c")));
    }

    [Fact]
    public void Un_jour_d_une_autre_annee_est_refuse() =>
        Should.Throw<DomainException>(() =>
            CalendrierAnnuel.Creer(2026).AjouterJour(new DateOnly(2027, 1, 2), "X", new LocalizedLabel("a", "b", "c")));

    [Fact]
    public void Les_jours_feries_legaux_ne_peuvent_pas_etre_retires() =>
        Should.Throw<DomainException>(() => CalendrierAnnuel.Creer(2026).RetirerJour("NOEL"));
}
