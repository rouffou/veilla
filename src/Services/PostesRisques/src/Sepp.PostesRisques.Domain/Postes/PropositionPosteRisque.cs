using Sepp.BuildingBlocks.Domain;

namespace Sepp.PostesRisques.Domain.Postes;

/// <summary>Ligne demandée d'une proposition : ajout, changement de niveau d'exposition ou retrait d'un risque.</summary>
public sealed record DemandeLienRisque(TypeModification Type, Guid RisqueId, string RisqueCode, NiveauExposition? NiveauExposition);

/// <summary>
/// Proposition de modification du profil de risques d'un poste (AFF-14), soumise en interne ou par l'employeur
/// via le portail (AFF-31, POR-03). Elle n'a d'effet qu'une fois validée par le CPMT, avec l'avis du Comité PPT
/// (date et pièce jointe). Les propositions refusées restent dans l'historique.
/// </summary>
public sealed class PropositionPosteRisque : AggregateRoot
{
    private readonly List<LignePropositionPosteRisque> _lignes = [];

    private PropositionPosteRisque()
    {
    }

    private PropositionPosteRisque(
        Guid id, Guid posteId, Guid affilieId, OrigineProposition origine, string proposeePar, DateTimeOffset dateProposition,
        string motif, DateOnly valideDu) : base(id)
    {
        PosteId = posteId;
        AffilieId = affilieId;
        Origine = origine;
        ProposeePar = proposeePar;
        DateProposition = dateProposition;
        Motif = motif;
        ValideDu = valideDu;
        Statut = StatutProposition.Soumise;
    }

    public Guid PosteId { get; private set; }

    public Guid AffilieId { get; private set; }

    public OrigineProposition Origine { get; private set; }

    /// <summary>Identifiant (sub) de l'auteur de la proposition.</summary>
    public string ProposeePar { get; private set; } = string.Empty;

    public DateTimeOffset DateProposition { get; private set; }

    public string Motif { get; private set; } = string.Empty;

    /// <summary>Date d'effet demandée pour le nouveau profil.</summary>
    public DateOnly ValideDu { get; private set; }

    public StatutProposition Statut { get; private set; }

    public IReadOnlyList<LignePropositionPosteRisque> Lignes => _lignes.AsReadOnly();

    /// <summary>Date de l'avis du Comité PPT (AFF-14).</summary>
    public DateOnly? DateAvisCppt { get; private set; }

    /// <summary>Pièce jointe de l'avis du Comité PPT.</summary>
    public Guid? DocumentAvisCpptId { get; private set; }

    /// <summary>Identifiant (sub) du CPMT qui a validé ou refusé.</summary>
    public string? DecidePar { get; private set; }

    public DateTimeOffset? DateDecision { get; private set; }

    public string? MotifRefus { get; private set; }

    public static PropositionPosteRisque Soumettre(
        Poste poste,
        OrigineProposition origine,
        string proposeePar,
        DateTimeOffset maintenant,
        string motif,
        DateOnly valideDu,
        IReadOnlyCollection<DemandeLienRisque> demandes)
    {
        if (poste.Statut != StatutPoste.Actif)
        {
            throw new DomainException($"Le poste « {poste.Intitule} » est archivé.");
        }

        if (demandes.Count == 0)
        {
            throw new DomainException("Une proposition doit contenir au moins une modification.");
        }

        if (demandes.GroupBy(d => d.RisqueId).Any(g => g.Count() > 1))
        {
            throw new DomainException("Un même risque ne peut figurer qu'une fois dans une proposition.");
        }

        var proposition = new PropositionPosteRisque(
            NewId(), poste.Id, poste.AffilieId, origine, Saisie.Obligatoire(proposeePar, "L'auteur", 100), maintenant,
            Saisie.Obligatoire(motif, "Le motif", 2000), valideDu);
        foreach (var demande in demandes)
        {
            if (demande.Type != TypeModification.Retrait && demande.NiveauExposition is null)
            {
                throw new DomainException($"Le niveau d'exposition au risque {demande.RisqueCode} est obligatoire.");
            }

            proposition._lignes.Add(new LignePropositionPosteRisque(
                NewId(),
                demande.Type,
                Saisie.Identifiant(demande.RisqueId, "Le risque"),
                Saisie.Code(demande.RisqueCode, "Le code du risque"),
                demande.Type == TypeModification.Retrait ? null : demande.NiveauExposition));
        }

        proposition.Raise(new PropositionPosteRisqueSoumise(proposition.Id, poste.Id, DateTimeOffset.UtcNow));
        return proposition;
    }

    /// <summary>Joint l'avis du Comité PPT (date et pièce jointe) avant la décision du CPMT.</summary>
    public void JoindreAvisCppt(DateOnly dateAvis, Guid documentId)
    {
        ExigerSoumise();
        DateAvisCppt = dateAvis;
        DocumentAvisCpptId = Saisie.Identifiant(documentId, "La pièce jointe de l'avis du Comité PPT");
        Raise(new PropositionPosteRisqueSoumise(Id, PosteId, DateTimeOffset.UtcNow));
    }

    /// <summary>AFF-14 : validation par le CPMT, qui exige l'avis du Comité PPT (date, pièce jointe).</summary>
    public void Valider(string cpmtId, DateTimeOffset maintenant, DateOnly? dateAvisCppt = null, Guid? documentAvisCpptId = null)
    {
        ExigerSoumise();
        if (dateAvisCppt is { } date && documentAvisCpptId is { } document)
        {
            JoindreAvisCppt(date, document);
        }
        else if (dateAvisCppt is not null || documentAvisCpptId is not null)
        {
            throw new DomainException("L'avis du Comité PPT comporte une date et une pièce jointe.");
        }

        if (DateAvisCppt is null || DocumentAvisCpptId is null)
        {
            throw new DomainException("L'avis du Comité PPT (date et pièce jointe) est obligatoire pour valider la modification.");
        }

        if (DateAvisCppt > DateOnly.FromDateTime(maintenant.UtcDateTime).AddDays(1))
        {
            throw new DomainException("La date de l'avis du Comité PPT ne peut pas être dans le futur.");
        }

        DecidePar = Saisie.Obligatoire(cpmtId, "Le CPMT", 100);
        DateDecision = maintenant;
        Statut = StatutProposition.Validee;
        Raise(new PropositionPosteRisqueDecidee(Id, PosteId, Statut, DateTimeOffset.UtcNow));
    }

    public void Refuser(string cpmtId, DateTimeOffset maintenant, string motif)
    {
        ExigerSoumise();
        DecidePar = Saisie.Obligatoire(cpmtId, "Le CPMT", 100);
        DateDecision = maintenant;
        MotifRefus = Saisie.Obligatoire(motif, "Le motif du refus", 2000);
        Statut = StatutProposition.Refusee;
        Raise(new PropositionPosteRisqueDecidee(Id, PosteId, Statut, DateTimeOffset.UtcNow));
    }

    private void ExigerSoumise()
    {
        if (Statut != StatutProposition.Soumise)
        {
            throw new DomainException($"La proposition a déjà été traitée ({Statut}).");
        }
    }
}

public sealed class LignePropositionPosteRisque : Entity
{
    private LignePropositionPosteRisque()
    {
    }

    internal LignePropositionPosteRisque(Guid id, TypeModification type, Guid risqueId, string risqueCode, NiveauExposition? niveauExposition)
        : base(id)
    {
        Type = type;
        RisqueId = risqueId;
        RisqueCode = risqueCode;
        NiveauExposition = niveauExposition;
    }

    public TypeModification Type { get; private set; }

    public Guid RisqueId { get; private set; }

    public string RisqueCode { get; private set; } = string.Empty;

    /// <summary>Niveau demandé ; absent pour un retrait.</summary>
    public NiveauExposition? NiveauExposition { get; private set; }
}

public sealed record PropositionPosteRisqueSoumise(Guid PropositionId, Guid PosteId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record PropositionPosteRisqueDecidee(Guid PropositionId, Guid PosteId, StatutProposition Statut, DateTimeOffset OccurredAt) : IDomainEvent;
