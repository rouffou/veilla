using Sepp.BuildingBlocks.Domain;
using Sepp.Obligations.Domain.Obligations;

using Shouldly;

namespace Sepp.Obligations.Domain.Tests;

/// <summary>SAN-02 : machine à états des statuts d'obligation.</summary>
public class MachineEtatsTests
{
    private static readonly StatutObligation[] Tous = Enum.GetValues<StatutObligation>();

    [Fact]
    public void Realise_est_definitif()
    {
        MachineEtatsObligation.Suivants(StatutObligation.Realise).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(StatutObligation.APlanifier, StatutObligation.Planifie)]
    [InlineData(StatutObligation.Planifie, StatutObligation.Convoque)]
    [InlineData(StatutObligation.Convoque, StatutObligation.Absent)]
    [InlineData(StatutObligation.Absent, StatutObligation.Planifie)]
    [InlineData(StatutObligation.Convoque, StatutObligation.Realise)]
    [InlineData(StatutObligation.Planifie, StatutObligation.APlanifier)]
    [InlineData(StatutObligation.APlanifier, StatutObligation.Reporte)]
    [InlineData(StatutObligation.Excuse, StatutObligation.Planifie)]
    [InlineData(StatutObligation.Annule, StatutObligation.APlanifier)]
    [InlineData(StatutObligation.SortiEntreprise, StatutObligation.APlanifier)]
    public void Transitions_autorisees(StatutObligation depuis, StatutObligation vers) =>
        MachineEtatsObligation.EstAutorisee(depuis, vers).ShouldBeTrue();

    [Theory]
    [InlineData(StatutObligation.Realise, StatutObligation.APlanifier)]
    [InlineData(StatutObligation.Realise, StatutObligation.Annule)]
    [InlineData(StatutObligation.APlanifier, StatutObligation.Convoque)]
    [InlineData(StatutObligation.APlanifier, StatutObligation.Absent)]
    [InlineData(StatutObligation.Annule, StatutObligation.Planifie)]
    [InlineData(StatutObligation.Annule, StatutObligation.Convoque)]
    [InlineData(StatutObligation.SortiEntreprise, StatutObligation.Convoque)]
    [InlineData(StatutObligation.Absent, StatutObligation.Convoque)]
    [InlineData(StatutObligation.Reporte, StatutObligation.Absent)]
    public void Transitions_interdites(StatutObligation depuis, StatutObligation vers) =>
        MachineEtatsObligation.EstAutorisee(depuis, vers).ShouldBeFalse();

    [Fact]
    public void Tout_statut_ouvert_peut_etre_realise_ou_annule_ou_sorti()
    {
        foreach (var statut in Tous.Where(MachineEtatsObligation.EstOuvert))
        {
            MachineEtatsObligation.EstAutorisee(statut, StatutObligation.Realise).ShouldBeTrue(statut.ToString());
            MachineEtatsObligation.EstAutorisee(statut, StatutObligation.SortiEntreprise).ShouldBeTrue(statut.ToString());
        }
    }

    [Fact]
    public void Les_statuts_ouverts_sont_ceux_qui_restent_a_honorer()
    {
        Tous.Where(MachineEtatsObligation.EstOuvert).ShouldBe(
        [
            StatutObligation.APlanifier, StatutObligation.Planifie, StatutObligation.Convoque, StatutObligation.Absent,
            StatutObligation.Reporte, StatutObligation.Excuse,
        ]);
    }

    [Fact]
    public void Un_statut_a_planifier_n_a_pas_de_rendez_vous_a_venir()
    {
        Tous.Where(MachineEtatsObligation.EstAPlanifier).ShouldBe(
            [StatutObligation.APlanifier, StatutObligation.Absent, StatutObligation.Reporte, StatutObligation.Excuse]);
        Tous.Where(MachineEtatsObligation.EstPlanifie).ShouldBe([StatutObligation.Planifie, StatutObligation.Convoque]);
    }

    [Fact]
    public void Depuis_tout_statut_ouvert_l_etat_realise_est_atteignable()
    {
        foreach (var depart in Tous.Where(MachineEtatsObligation.EstOuvert))
        {
            var vus = new HashSet<StatutObligation> { depart };
            var file = new Queue<StatutObligation>([depart]);
            while (file.Count > 0)
            {
                foreach (var suivant in MachineEtatsObligation.Suivants(file.Dequeue()).Where(vus.Add))
                {
                    file.Enqueue(suivant);
                }
            }

            vus.ShouldContain(StatutObligation.Realise, depart.ToString());
        }
    }

    [Theory]
    [InlineData(StatutObligation.Realise)]
    [InlineData(StatutObligation.Annule)]
    [InlineData(StatutObligation.SortiEntreprise)]
    public void Les_statuts_clos_ne_sont_pas_ouverts(StatutObligation statut) =>
        MachineEtatsObligation.EstOuvert(statut).ShouldBeFalse();

    [Fact]
    public void L_agregat_refuse_une_transition_hors_machine()
    {
        var obligation = Fabrique.AuStatut(StatutObligation.Realise);

        var ex = Should.Throw<DomainException>(() => obligation.Excuser());

        ex.Message.ShouldContain("Realise");
    }

    [Fact]
    public void Chaque_action_de_l_agregat_respecte_la_machine_a_etats()
    {
        // Pour chaque statut de départ, l'action n'est possible que si la machine l'autorise.
        foreach (var depart in Tous)
        {
            var autorise = MachineEtatsObligation.EstAutorisee(depart, StatutObligation.Excuse);
            var obligation = Fabrique.AuStatut(depart);
            if (autorise)
            {
                obligation.Excuser();
                obligation.Statut.ShouldBe(StatutObligation.Excuse);
            }
            else
            {
                Should.Throw<DomainException>(() => obligation.Excuser(), depart.ToString());
            }
        }
    }
}
