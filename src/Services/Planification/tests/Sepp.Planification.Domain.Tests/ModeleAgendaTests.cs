using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Domain.Calendar;
using Sepp.Planification.Domain.Agenda;

using Shouldly;

namespace Sepp.Planification.Domain.Tests;

/// <summary>PLA-03 : modèles d'agenda et durées standard par type d'acte.</summary>
public class ModeleAgendaTests
{
    private static readonly BusinessCalendar Calendrier = BusinessCalendar.Belgian(2027);

    private static ModeleAgenda Modele()
    {
        var modele = ModeleAgenda.Creer(Fabrique.Conseiller, Fabrique.Lieu, new Validity(new DateOnly(2027, 1, 1)));
        modele.AjouterPlage(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(11, 0), "evaluation-periodique", 30, false, true, null);
        modele.AjouterPlage(DayOfWeek.Monday, new TimeOnly(14, 0), new TimeOnly(15, 0), "EXAMEN_REPRISE", 20, true, true, null);
        return modele;
    }

    [Fact]
    public void Une_plage_est_decoupee_en_creneaux_de_la_duree_standard()
    {
        var creneaux = Modele().Projeter(Fabrique.Lundi, Fabrique.Lundi, Calendrier.IsBusinessDay).ToList();

        creneaux.Count(c => c.TypeActe == "EVALUATION_PERIODIQUE").ShouldBe(4);
        creneaux.Count(c => c.TypeActe == "EXAMEN_REPRISE").ShouldBe(3);
        creneaux[0].Debut.ShouldBe(Fabrique.Instant(Fabrique.Lundi, 9));
        creneaux[0].Fin.ShouldBe(Fabrique.Instant(Fabrique.Lundi, 9, 30));
    }

    [Fact]
    public void Les_creneaux_d_urgence_ne_sont_pas_ouverts_en_ligne()
    {
        var creneaux = Modele().Projeter(Fabrique.Lundi, Fabrique.Lundi, Calendrier.IsBusinessDay).ToList();

        creneaux.Where(c => c.ReserveUrgence).ShouldAllBe(c => !c.OuvertEnLigne);
        creneaux.Where(c => !c.ReserveUrgence).ShouldAllBe(c => c.OuvertEnLigne);
    }

    [Fact]
    public void Les_jours_feries_et_les_jours_hors_validite_ne_donnent_pas_de_creneau()
    {
        var modele = Modele();

        // Lundi de Pâques 2027 (29 mars) : férié.
        modele.Projeter(new DateOnly(2027, 3, 29), new DateOnly(2027, 3, 29), Calendrier.IsBusinessDay).ShouldBeEmpty();

        modele.Cloturer(new DateOnly(2027, 3, 15));
        modele.Projeter(Fabrique.Lundi, Fabrique.Lundi, Calendrier.IsBusinessDay).ShouldBeEmpty();
    }

    [Fact]
    public void Les_jours_de_presence_sont_ceux_des_plages()
    {
        var modele = Modele();
        modele.AjouterPlage(DayOfWeek.Thursday, new TimeOnly(9, 0), new TimeOnly(12, 0), "EVALUATION_PERIODIQUE", 30, false, false, null);

        modele.JoursPresence.ShouldBe([DayOfWeek.Monday, DayOfWeek.Thursday]);
    }

    [Fact]
    public void Des_plages_qui_se_chevauchent_sont_refusees()
    {
        var modele = Modele();

        Should.Throw<DomainException>(() => modele.AjouterPlage(DayOfWeek.Monday, new TimeOnly(10, 30), new TimeOnly(12, 0), "EVALUATION_PERIODIQUE", 30, false, false, null));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(500)]
    [InlineData(150)]
    public void La_duree_d_un_creneau_doit_etre_raisonnable_et_tenir_dans_la_plage(int duree)
    {
        var modele = ModeleAgenda.Creer(Fabrique.Conseiller, Fabrique.Lieu, new Validity(new DateOnly(2027, 1, 1)));

        Should.Throw<DomainException>(() => modele.AjouterPlage(DayOfWeek.Friday, new TimeOnly(9, 0), new TimeOnly(11, 0), "EVALUATION_PERIODIQUE", duree, false, false, null));
    }

    [Fact]
    public void La_duree_propre_a_un_cpmt_l_emporte_sur_la_duree_globale()
    {
        var autre = Guid.CreateVersion7();
        List<DureeStandard> durees =
        [
            DureeStandard.Creer("EVALUATION_PERIODIQUE", null, 30),
            DureeStandard.Creer("EVALUATION_PERIODIQUE", Fabrique.Conseiller, 45),
            DureeStandard.Creer("EXAMEN_REPRISE", null, 20),
        ];

        DureeStandard.Resoudre(durees, "EVALUATION_PERIODIQUE", Fabrique.Conseiller).ShouldBe(45);
        DureeStandard.Resoudre(durees, "EVALUATION_PERIODIQUE", autre).ShouldBe(30);
        DureeStandard.Resoudre(durees, "EXAMEN_REPRISE", Fabrique.Conseiller).ShouldBe(20);
        DureeStandard.Resoudre(durees, "INCONNU", Fabrique.Conseiller).ShouldBeNull();
    }

    [Fact]
    public void Un_code_de_type_d_acte_est_normalise_ou_refuse()
    {
        CodeMetier.Normaliser("examen-reprise", "Type d'acte").ShouldBe("EXAMEN_REPRISE");
        Should.Throw<DomainException>(() => CodeMetier.Normaliser("examen reprise", "Type d'acte"));
        Should.Throw<DomainException>(() => CodeMetier.Normaliser("  ", "Type d'acte"));
    }
}
