using Sepp.BuildingBlocks.Domain;

using Sepp.PostesRisques.Domain.Risques;

namespace Sepp.PostesRisques.Domain.Listes;

/// <summary>Types de listes nominatives légales (AFF-30).</summary>
public enum TypeListeNominative
{
    PosteSecurite,
    PosteVigilance,
    ActiviteRisqueDefini,
    ActiviteDenreesAlimentaires,
    TravailNuit,
}

/// <summary>Provenance d'une ligne de liste.</summary>
public enum OrigineLigne
{
    /// <summary>Déduite des postes, de leurs risques et des affectations.</summary>
    Calcul,

    /// <summary>Ajustement proposé (par l'employeur) et validé par le CPMT (AFF-31).</summary>
    AjustementValide,
}

/// <summary>Ligne à inscrire sur une liste : travailleur (identifiant seul, ARC-06/DAT-06), poste, risques, dernière évaluation.</summary>
public sealed record LigneCalculee(
    Guid PersonneId,
    Guid PosteId,
    IReadOnlyList<string> CodesRisques,
    DateOnly? DateDerniereEvaluation,
    OrigineLigne Origine);

/// <summary>
/// AFF-30 : liste nominative légale d'un affilié pour un type de surveillance, à une date de référence.
/// Chaque génération crée une nouvelle version (jamais de réécriture) ; les versions sont conservées au moins
/// le délai légal (AFF-31, paramètre SANTE.LISTES_NOMINATIVES.CONSERVATION).
/// Les travailleurs n'y figurent que par leur identifiant : l'identité reste dans le service Personnes (DAT-06).
/// </summary>
public sealed class ListeNominative : AggregateRoot
{
    private static readonly Dictionary<TypeListeNominative, CategorieRisque[]> CategoriesParType = new()
    {
        [TypeListeNominative.PosteSecurite] = [CategorieRisque.PosteSecurite, CategorieRisque.Conduite],
        [TypeListeNominative.PosteVigilance] = [CategorieRisque.PosteVigilance],
        [TypeListeNominative.ActiviteRisqueDefini] =
        [
            CategorieRisque.Chimique, CategorieRisque.Physique, CategorieRisque.Biologique, CategorieRisque.Ergonomique,
            CategorieRisque.Psychosocial,
        ],
        [TypeListeNominative.ActiviteDenreesAlimentaires] = [CategorieRisque.DenreesAlimentaires],
        [TypeListeNominative.TravailNuit] = [CategorieRisque.TravailNuit],
    };

    private readonly List<LigneListeNominative> _lignes = [];

    private ListeNominative()
    {
    }

    private ListeNominative(
        Guid id, Guid affilieId, TypeListeNominative type, int version, DateOnly dateReference, DateTimeOffset dateGeneration,
        string genereePar, DateOnly conserverJusquAu, Guid? propositionId) : base(id)
    {
        AffilieId = affilieId;
        Type = type;
        Version = version;
        DateReference = dateReference;
        DateGeneration = dateGeneration;
        GenereePar = genereePar;
        ConserverJusquAu = conserverJusquAu;
        PropositionId = propositionId;
    }

    public Guid AffilieId { get; private set; }

    public TypeListeNominative Type { get; private set; }

    /// <summary>Numéro de version pour le couple (affilié, type), à partir de 1.</summary>
    public int Version { get; private set; }

    /// <summary>Date à laquelle les postes, risques et affectations sont évalués.</summary>
    public DateOnly DateReference { get; private set; }

    public DateTimeOffset DateGeneration { get; private set; }

    public string GenereePar { get; private set; } = string.Empty;

    /// <summary>Document produit à partir de la liste (service Documents), s'il existe.</summary>
    public Guid? DocumentId { get; private set; }

    /// <summary>Date avant laquelle la version ne peut pas être purgée (AFF-31).</summary>
    public DateOnly ConserverJusquAu { get; private set; }

    /// <summary>Proposition de modification validée dont la version est issue, le cas échéant.</summary>
    public Guid? PropositionId { get; private set; }

    public IReadOnlyList<LigneListeNominative> Lignes => _lignes.AsReadOnly();

    /// <summary>Catégories de risques qui font figurer un travailleur sur une liste d'un type donné.</summary>
    public static IReadOnlySet<CategorieRisque> CategoriesDe(TypeListeNominative type) => CategoriesParType[type].ToHashSet();

    public static ListeNominative Generer(
        Guid affilieId,
        TypeListeNominative type,
        int version,
        DateOnly dateReference,
        DateTimeOffset maintenant,
        string genereePar,
        int conservationAnnees,
        IEnumerable<LigneCalculee> lignes,
        Guid? propositionId = null)
    {
        if (version < 1)
        {
            throw new DomainException("La version d'une liste nominative commence à 1.");
        }

        if (conservationAnnees < 1)
        {
            throw new DomainException("La durée de conservation d'une liste nominative doit être d'au moins un an.");
        }

        var liste = new ListeNominative(
            NewId(), Saisie.Identifiant(affilieId, "L'affilié"), type, version, dateReference, maintenant,
            Saisie.Obligatoire(genereePar, "L'auteur de la génération", 100),
            DateOnly.FromDateTime(maintenant.UtcDateTime).AddYears(conservationAnnees), propositionId);

        foreach (var ligne in lignes
                     .GroupBy(l => (l.PersonneId, l.PosteId))
                     .Select(g => g.First())
                     .OrderBy(l => l.PosteId)
                     .ThenBy(l => l.PersonneId))
        {
            liste._lignes.Add(new LigneListeNominative(
                NewId(),
                Saisie.Identifiant(ligne.PersonneId, "Le travailleur"),
                Saisie.Identifiant(ligne.PosteId, "Le poste"),
                ligne.CodesRisques.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
                ligne.DateDerniereEvaluation,
                ligne.Origine));
        }

        liste.Raise(new ListeNominativeGeneree(liste.Id, liste.AffilieId, type, version, DateTimeOffset.UtcNow));
        return liste;
    }

    public bool Contient(Guid personneId, Guid posteId) => _lignes.Any(l => l.PersonneId == personneId && l.PosteId == posteId);

    /// <summary>Associe le document produit (PDF de la liste) à cette version.</summary>
    public void AssocierDocument(Guid documentId)
    {
        DocumentId = Saisie.Identifiant(documentId, "Le document");
        Raise(new ListeNominativeGeneree(Id, AffilieId, Type, Version, DateTimeOffset.UtcNow));
    }
}

public sealed class LigneListeNominative : Entity
{
    private LigneListeNominative()
    {
    }

    internal LigneListeNominative(
        Guid id, Guid personneId, Guid posteId, IReadOnlyList<string> codesRisques, DateOnly? dateDerniereEvaluation, OrigineLigne origine)
        : base(id)
    {
        PersonneId = personneId;
        PosteId = posteId;
        CodesRisques = codesRisques;
        DateDerniereEvaluation = dateDerniereEvaluation;
        Origine = origine;
    }

    public Guid PersonneId { get; private set; }

    public Guid PosteId { get; private set; }

    public IReadOnlyList<string> CodesRisques { get; private set; } = [];

    /// <summary>Date de la dernière évaluation de santé connue (projection de ExamenCloture).</summary>
    public DateOnly? DateDerniereEvaluation { get; private set; }

    public OrigineLigne Origine { get; private set; }

    public LigneCalculee VersLigneCalculee() => new(PersonneId, PosteId, CodesRisques, DateDerniereEvaluation, Origine);
}

public sealed record ListeNominativeGeneree(Guid ListeId, Guid AffilieId, TypeListeNominative Type, int Version, DateTimeOffset OccurredAt) : IDomainEvent;
