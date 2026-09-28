using Sepp.BuildingBlocks.Domain;

using Sepp.PostesRisques.Domain.Risques;

namespace Sepp.PostesRisques.Domain.Surcharges;

/// <summary>Cible d'une surcharge de fréquence (AFF-13).</summary>
public enum CibleSurcharge
{
    /// <summary>Un poste du catalogue de l'affilié.</summary>
    Poste,

    /// <summary>Un groupe d'exposition homogène (service Prévention).</summary>
    Groupe,

    /// <summary>Un travailleur (identifiant du service Personnes, sans donnée d'identité).</summary>
    Personne,
}

/// <summary>
/// AFF-13 : le CPMT surcharge la fréquence de surveillance d'un risque pour un poste, un groupe ou un travailleur,
/// avec un motif et une période de validité (§15.3 surcharge_frequence).
/// </summary>
public sealed class SurchargeFrequence : AggregateRoot
{
    private SurchargeFrequence()
    {
    }

    private SurchargeFrequence(
        Guid id, Guid affilieId, CibleSurcharge cibleType, Guid cibleId, Guid risqueId, string risqueCode, int frequenceMois,
        string motif, string cpmtId, Validity validite) : base(id)
    {
        AffilieId = affilieId;
        CibleType = cibleType;
        CibleId = cibleId;
        RisqueId = risqueId;
        RisqueCode = risqueCode;
        FrequenceMois = frequenceMois;
        Motif = motif;
        CpmtId = cpmtId;
        Validite = validite;
    }

    public Guid AffilieId { get; private set; }

    public CibleSurcharge CibleType { get; private set; }

    public Guid CibleId { get; private set; }

    public Guid RisqueId { get; private set; }

    public string RisqueCode { get; private set; } = string.Empty;

    public int FrequenceMois { get; private set; }

    public string Motif { get; private set; } = string.Empty;

    /// <summary>Identifiant (sub) du CPMT auteur de la surcharge.</summary>
    public string CpmtId { get; private set; } = string.Empty;

    public Validity Validite { get; private set; }

    public static SurchargeFrequence Definir(
        Guid affilieId, CibleSurcharge cibleType, Guid cibleId, Risque risque, int frequenceMois, string motif, string cpmtId,
        DateOnly valideDu, DateOnly? valideJusquAu)
    {
        if (frequenceMois is < 1 or > RegleSurveillance.FrequenceMaximaleMois)
        {
            throw new DomainException($"La fréquence doit être comprise entre 1 et {RegleSurveillance.FrequenceMaximaleMois} mois.");
        }

        var surcharge = new SurchargeFrequence(
            NewId(),
            Saisie.Identifiant(affilieId, "L'affilié"),
            cibleType,
            Saisie.Identifiant(cibleId, "La cible"),
            risque.Id,
            risque.Code,
            frequenceMois,
            Saisie.Obligatoire(motif, "Le motif", 2000),
            Saisie.Obligatoire(cpmtId, "Le CPMT", 100),
            new Validity(valideDu, valideJusquAu));
        surcharge.Raise(new SurchargeFrequenceModifiee(surcharge.Id, DateTimeOffset.UtcNow));
        return surcharge;
    }

    /// <summary>Met fin à la surcharge à une date (exclusive) ; elle reste dans l'historique (DAT-04).</summary>
    public void Cloturer(DateOnly fin)
    {
        if (Validite.ValidTo is { } finActuelle && fin >= finActuelle)
        {
            throw new DomainException($"La surcharge prend déjà fin le {finActuelle:yyyy-MM-dd}.");
        }

        Validite = Validite.CloseAt(fin);
        Raise(new SurchargeFrequenceModifiee(Id, DateTimeOffset.UtcNow));
    }

    public bool Chevauche(CibleSurcharge cibleType, Guid cibleId, Guid risqueId, Validity periode) =>
        CibleType == cibleType && CibleId == cibleId && RisqueId == risqueId && Validite.Overlaps(periode);
}

public sealed record SurchargeFrequenceModifiee(Guid SurchargeId, DateTimeOffset OccurredAt) : IDomainEvent;
