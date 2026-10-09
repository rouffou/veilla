using Sepp.Planification.Domain.Projections;

using Shouldly;

namespace Sepp.Planification.Domain.Tests;

/// <summary>ARC-33, DAT-08 : clôture de la projection des obligations et calendrier local des jours fériés.</summary>
public class ProjectionsTests
{
    private static readonly DateTimeOffset T0 = new(2027, 3, 1, 8, 0, 0, TimeSpan.Zero);

    private static ObligationAPlanifier Obligation() =>
        new(Guid.CreateVersion7(), Fabrique.Personne, Fabrique.Affilie, "EXAMEN_REPRISE", Fabrique.Lundi, Fabrique.Lundi.AddDays(14), T0);

    [Fact]
    public void Une_obligation_close_n_est_plus_a_planifier_meme_sans_rendez_vous()
    {
        var obligation = Obligation();
        obligation.EstAPlanifier.ShouldBeTrue();

        obligation.Cloturer("Annule", Fabrique.Lundi, T0.AddHours(1)).ShouldBeTrue();

        obligation.Cloturee.ShouldBeTrue();
        obligation.StatutCloture.ShouldBe("Annule");
        obligation.EstAPlanifier.ShouldBeFalse();
    }

    [Fact]
    public void La_cloture_est_idempotente()
    {
        var obligation = Obligation();
        obligation.Cloturer("Realise", Fabrique.Lundi, T0.AddHours(1));

        obligation.Cloturer("Annule", Fabrique.Lundi.AddDays(1), T0.AddHours(2)).ShouldBeFalse();

        obligation.StatutCloture.ShouldBe("Realise");
        obligation.DateCloture.ShouldBe(Fabrique.Lundi);
    }

    [Fact]
    public void Une_creation_plus_ancienne_livree_apres_la_cloture_est_ignoree()
    {
        var obligation = Obligation();
        obligation.Cloturer("Annule", Fabrique.Lundi, T0.AddHours(1));

        obligation.Appliquer(Fabrique.Personne, Fabrique.Affilie, "EXAMEN_REPRISE", Fabrique.Lundi, null, T0).ShouldBeFalse();
    }

    [Fact]
    public void Le_calendrier_local_garde_l_etat_le_plus_recent()
    {
        var calendrier = new CalendrierLocal(2027, [new DateOnly(2027, 5, 3), new DateOnly(2027, 5, 3)], T0);
        calendrier.JoursSupplementaires.ShouldBe([new DateOnly(2027, 5, 3)]);

        calendrier.Appliquer([new DateOnly(2027, 11, 12)], T0.AddMinutes(1)).ShouldBeTrue();
        calendrier.Appliquer([new DateOnly(2027, 1, 4)], T0).ShouldBeFalse();

        calendrier.JoursSupplementaires.ShouldBe([new DateOnly(2027, 11, 12)]);
    }
}
