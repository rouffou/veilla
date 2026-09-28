using Sepp.BuildingBlocks.Domain;

namespace Sepp.Affilies.Domain.Historique;

/// <summary>
/// AFF-05 — Entrée de l'historique de la fiche d'un affilié : qui, quand, quelle version, quelle action,
/// valeurs avant et après (JSON, limité aux éléments modifiés). Écrite dans la même transaction que la modification,
/// jamais modifiée ensuite.
/// </summary>
public sealed class ModificationAffilie : Entity
{
    private ModificationAffilie()
    {
    }

    private ModificationAffilie(Guid id, Guid affilieId, int numeroVersion, string action, string auteur, DateTimeOffset horodatage, string? avant, string? apres)
        : base(id)
    {
        AffilieId = affilieId;
        NumeroVersion = numeroVersion;
        Action = action;
        Auteur = auteur;
        Horodatage = horodatage;
        Avant = avant;
        Apres = apres;
    }

    public Guid AffilieId { get; private set; }

    /// <summary>Version de la fiche après la modification.</summary>
    public int NumeroVersion { get; private set; }

    /// <summary>Code de l'action métier (par ex. <c>contact.ajoute</c>).</summary>
    public string Action { get; private set; } = string.Empty;

    /// <summary>Identifiant de l'utilisateur (claim <c>sub</c>) ou « system ».</summary>
    public string Auteur { get; private set; } = string.Empty;

    /// <summary>Horodatage UTC.</summary>
    public DateTimeOffset Horodatage { get; private set; }

    /// <summary>Valeurs avant la modification (JSON), <c>null</c> pour une création.</summary>
    public string? Avant { get; private set; }

    /// <summary>Valeurs après la modification (JSON).</summary>
    public string? Apres { get; private set; }

    public static ModificationAffilie Enregistrer(Guid affilieId, int numeroVersion, string action, string auteur, DateTimeOffset horodatage, string? avant, string? apres)
    {
        if (affilieId == Guid.Empty || numeroVersion <= 0 || string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(auteur))
        {
            throw new DomainException("Une entrée d'historique identifie l'affilié, la version, l'action et l'auteur.");
        }

        return new ModificationAffilie(NewId(), affilieId, numeroVersion, action, auteur, horodatage.ToUniversalTime(), avant, apres);
    }
}
