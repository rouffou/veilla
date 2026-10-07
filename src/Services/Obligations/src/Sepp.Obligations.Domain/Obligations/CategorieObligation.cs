namespace Sepp.Obligations.Domain.Obligations;

/// <summary>
/// Catégorie d'une obligation ouverte pour le tableau de bord de l'employeur (POR-02) : les catégories sont exclusives.
/// </summary>
public enum CategorieObligation
{
    /// <summary>La date limite est dépassée.</summary>
    EnRetard,

    /// <summary>Un rendez-vous (planifié ou convoqué) couvre l'obligation et sa date limite n'est pas dépassée.</summary>
    Planifiee,

    /// <summary>À planifier, due dans l'horizon (ou déjà due) : l'employeur peut agir.</summary>
    Due,

    /// <summary>À planifier, due au-delà de l'horizon.</summary>
    AVenir,
}

public static class Classement
{
    /// <returns>La catégorie d'une obligation ouverte, <c>null</c> si l'obligation est close (réalisée, annulée, sortie).</returns>
    public static CategorieObligation? Classer(Obligation obligation, DateOnly aujourdHui, int horizonDuesJours)
    {
        if (!obligation.EstOuverte)
        {
            return null;
        }

        if (obligation.EstEnRetardAu(aujourdHui))
        {
            return CategorieObligation.EnRetard;
        }

        if (MachineEtatsObligation.EstPlanifie(obligation.Statut))
        {
            return CategorieObligation.Planifiee;
        }

        return obligation.DateDue <= aujourdHui.AddDays(horizonDuesJours) ? CategorieObligation.Due : CategorieObligation.AVenir;
    }
}
