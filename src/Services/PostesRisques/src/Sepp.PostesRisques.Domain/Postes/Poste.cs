using Sepp.BuildingBlocks.Domain;

namespace Sepp.PostesRisques.Domain.Postes;

public enum StatutPoste
{
    Actif,
    Archive,
}

/// <summary>Niveau d'exposition du poste au risque (§15.3 poste_risque).</summary>
public enum NiveauExposition
{
    Faible,
    Moyen,
    Eleve,
}

/// <summary>
/// Poste ou fonction du catalogue propre à l'affilié (AFF-10), rattaché à un métier type du référentiel commun
/// (code de la nomenclature des métiers types du service Référentiels, stocké tel quel, sans appel synchrone).
/// Le profil de risques du poste (<see cref="PosteRisque"/>) ne change que par une proposition validée par le CPMT (AFF-14).
/// </summary>
public sealed class Poste : AggregateRoot
{
    private readonly List<PosteRisque> _risques = [];

    private Poste()
    {
    }

    private Poste(Guid id, Guid affilieId, string intitule, string? description, string? metierTypeCode) : base(id)
    {
        AffilieId = affilieId;
        Intitule = intitule;
        Description = description;
        MetierTypeCode = metierTypeCode;
        Statut = StatutPoste.Actif;
    }

    /// <summary>Affilié propriétaire du catalogue (référence par identifiant vers le service Affiliés).</summary>
    public Guid AffilieId { get; private set; }

    public string Intitule { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>Code du métier type (nomenclature partagée du service Référentiels).</summary>
    public string? MetierTypeCode { get; private set; }

    public StatutPoste Statut { get; private set; }

    /// <summary>Historique complet des liens poste ↔ risque (DAT-04).</summary>
    public IReadOnlyList<PosteRisque> Risques => _risques.AsReadOnly();

    public static Poste Creer(Guid affilieId, string intitule, string? description, string? metierTypeCode)
    {
        var poste = new Poste(
            NewId(),
            Saisie.Identifiant(affilieId, "L'affilié"),
            Saisie.Obligatoire(intitule, "L'intitulé du poste", 200),
            Saisie.Facultatif(description, "La description du poste", 2000),
            Saisie.CodeFacultatif(metierTypeCode, "Le code du métier type"));
        poste.Raise(new PosteModifie(poste.Id, poste.AffilieId, DateTimeOffset.UtcNow));
        return poste;
    }

    public void Modifier(string intitule, string? description, string? metierTypeCode)
    {
        ExigerActif();
        Intitule = Saisie.Obligatoire(intitule, "L'intitulé du poste", 200);
        Description = Saisie.Facultatif(description, "La description du poste", 2000);
        MetierTypeCode = Saisie.CodeFacultatif(metierTypeCode, "Le code du métier type");
        Raise(new PosteModifie(Id, AffilieId, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Archive le poste. Un poste encore exposé à des risques ne peut pas être archivé : le retrait des risques
    /// est une modification du lien poste ↔ risque et passe par une proposition validée par le CPMT (AFF-14).
    /// </summary>
    public void Archiver(DateOnly date)
    {
        ExigerActif();
        if (_risques.Any(r => r.Validite.IsOpen || r.Validite.Contains(date)))
        {
            throw new DomainException($"Le poste « {Intitule} » est encore exposé à des risques : faites valider leur retrait par le CPMT avant de l'archiver.");
        }

        Statut = StatutPoste.Archive;
        Raise(new PosteModifie(Id, AffilieId, DateTimeOffset.UtcNow));
    }

    public void Reactiver()
    {
        if (Statut == StatutPoste.Actif)
        {
            throw new DomainException($"Le poste « {Intitule} » est déjà actif.");
        }

        Statut = StatutPoste.Actif;
        Raise(new PosteModifie(Id, AffilieId, DateTimeOffset.UtcNow));
    }

    /// <summary>Liens poste ↔ risque en vigueur à une date.</summary>
    public IEnumerable<PosteRisque> RisquesAu(DateOnly date) =>
        _risques.Where(r => r.Validite.Contains(date)).OrderBy(r => r.RisqueCode, StringComparer.Ordinal);

    /// <summary>
    /// AFF-14 : applique une proposition validée par le CPMT. Les liens modifiés ou retirés sont clôturés à la date
    /// d'effet, les nouveaux liens portent le CPMT validant et l'avis du Comité PPT (date, pièce jointe).
    /// </summary>
    public void AppliquerProposition(PropositionPosteRisque proposition)
    {
        ExigerActif();
        if (proposition.PosteId != Id)
        {
            throw new DomainException("La proposition ne concerne pas ce poste.");
        }

        if (proposition.Statut != StatutProposition.Validee || proposition.DecidePar is null ||
            proposition.DateAvisCppt is not { } dateAvis || proposition.DocumentAvisCpptId is not { } documentAvis)
        {
            throw new DomainException("Seule une proposition validée par le CPMT, avec l'avis du Comité PPT, peut modifier le profil de risques.");
        }

        var effet = proposition.ValideDu;
        foreach (var ligne in proposition.Lignes)
        {
            var ouvert = _risques.SingleOrDefault(r => r.RisqueId == ligne.RisqueId && r.Validite.IsOpen);
            switch (ligne.Type)
            {
                case TypeModification.Ajout when ouvert is not null:
                    throw new DomainException($"Le poste « {Intitule} » est déjà exposé au risque {ligne.RisqueCode}.");
                case TypeModification.Modification or TypeModification.Retrait when ouvert is null:
                    throw new DomainException($"Le poste « {Intitule} » n'est pas exposé au risque {ligne.RisqueCode}.");
                case TypeModification.Modification or TypeModification.Retrait:
                    if (effet <= ouvert!.Validite.ValidFrom)
                    {
                        throw new DomainException(
                            $"Le lien avec le risque {ligne.RisqueCode} ne peut être modifié qu'après le {ouvert.Validite.ValidFrom:yyyy-MM-dd}.");
                    }

                    ouvert.Cloturer(effet);
                    break;
            }

            if (ligne.Type != TypeModification.Retrait)
            {
                _risques.Add(new PosteRisque(
                    NewId(), ligne.RisqueId, ligne.RisqueCode, ligne.NiveauExposition!.Value, proposition.DecidePar,
                    dateAvis, documentAvis, proposition.Id, new Validity(effet)));
            }
        }

        Raise(new ProfilRisquesModifie(Id, AffilieId, effet, DateTimeOffset.UtcNow));
    }

    private void ExigerActif()
    {
        if (Statut != StatutPoste.Actif)
        {
            throw new DomainException($"Le poste « {Intitule} » est archivé.");
        }
    }
}

/// <summary>Lien poste ↔ risque validé par le CPMT (AFF-11, AFF-14, §15.3 poste_risque).</summary>
public sealed class PosteRisque : Entity
{
    private PosteRisque()
    {
    }

    internal PosteRisque(
        Guid id,
        Guid risqueId,
        string risqueCode,
        NiveauExposition niveauExposition,
        string valideParCpmtId,
        DateOnly dateAvisCppt,
        Guid documentAvisCpptId,
        Guid propositionId,
        Validity validite) : base(id)
    {
        RisqueId = risqueId;
        RisqueCode = risqueCode;
        NiveauExposition = niveauExposition;
        ValideParCpmtId = valideParCpmtId;
        DateAvisCppt = dateAvisCppt;
        DocumentAvisCpptId = documentAvisCpptId;
        PropositionId = propositionId;
        Validite = validite;
    }

    public Guid RisqueId { get; private set; }

    /// <summary>Code du risque, recopié pour les événements (ProfilRisquePosteModifie).</summary>
    public string RisqueCode { get; private set; } = string.Empty;

    public NiveauExposition NiveauExposition { get; private set; }

    /// <summary>Identifiant (sub) du CPMT qui a validé le lien.</summary>
    public string ValideParCpmtId { get; private set; } = string.Empty;

    /// <summary>Date de l'avis du Comité PPT.</summary>
    public DateOnly DateAvisCppt { get; private set; }

    /// <summary>Pièce jointe de l'avis du Comité PPT (document du service Documents).</summary>
    public Guid DocumentAvisCpptId { get; private set; }

    /// <summary>Proposition à l'origine du lien (traçabilité).</summary>
    public Guid PropositionId { get; private set; }

    public Validity Validite { get; private set; }

    internal void Cloturer(DateOnly fin) => Validite = Validite.CloseAt(fin);
}

public sealed record PosteModifie(Guid PosteId, Guid AffilieId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record ProfilRisquesModifie(Guid PosteId, Guid AffilieId, DateOnly ValideDu, DateTimeOffset OccurredAt) : IDomainEvent;
