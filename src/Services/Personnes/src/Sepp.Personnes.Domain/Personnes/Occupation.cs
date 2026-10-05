using Sepp.BuildingBlocks.Domain;

namespace Sepp.Personnes.Domain.Personnes;

/// <summary>AFF-23 : catégorie de travailleur (cas particuliers).</summary>
public enum TypeTravailleur
{
    Salarie,

    /// <summary>Occupé par une agence d'intérim (affilié) et mis à disposition d'un utilisateur (affilié utilisateur).</summary>
    Interimaire,
    Etudiant,
    Stagiaire,
    Detache,
    Benevole,
}

/// <summary>AFF-20 : type de contrat déclaré (DIMONA / DmfA ou saisie).</summary>
public enum TypeContrat
{
    DureeIndeterminee,
    DureeDeterminee,
    TravailNettementDefini,
    Remplacement,
    Interim,
    Etudiant,
    Flexijob,
    Occasionnel,
    Stage,
    Benevolat,
    Autre,
}

/// <summary>
/// Occupation d'une personne chez un affilié (§15.3), issue de DIMONA (AFF-20) ou d'un import (AFF-21).
/// Période du <see cref="DateDebut"/> au <see cref="DateFin"/> inclus (dernier jour travaillé, comme DIMONA).
/// </summary>
public sealed class Occupation : Entity
{
    private readonly List<Affectation> _affectations = [];

    private Occupation()
    {
    }

    private Occupation(Guid id, NouvelleOccupation nouvelle, string? referenceDimona) : base(id)
    {
        AffilieId = nouvelle.AffilieId;
        AffilieUtilisateurId = nouvelle.AffilieUtilisateurId;
        TypeTravailleur = nouvelle.TypeTravailleur;
        TypeContrat = nouvelle.TypeContrat;
        DateDebut = nouvelle.DateDebut;
        DateFin = nouvelle.DateFin;
        ReferenceDimona = referenceDimona;
    }

    /// <summary>Employeur (pour un intérimaire : l'agence d'intérim).</summary>
    public Guid AffilieId { get; private set; }

    /// <summary>AFF-23 : entreprise utilisatrice d'un intérimaire, responsable des risques du poste.</summary>
    public Guid? AffilieUtilisateurId { get; private set; }

    public TypeTravailleur TypeTravailleur { get; private set; }

    public TypeContrat TypeContrat { get; private set; }

    public DateOnly DateDebut { get; private set; }

    /// <summary>Dernier jour d'occupation (inclus) ; <c>null</c> tant que la sortie n'est pas déclarée.</summary>
    public DateOnly? DateFin { get; private set; }

    /// <summary>Numéro de la déclaration DIMONA, unique : clé d'idempotence de l'alimentation automatique (AFF-20).</summary>
    public string? ReferenceDimona { get; private set; }

    public IReadOnlyList<Affectation> Affectations => _affectations.AsReadOnly();

    public bool EstActiveAu(DateOnly date) => date >= DateDebut && (DateFin is null || date <= DateFin);

    internal static Occupation Creer(Guid id, NouvelleOccupation nouvelle)
    {
        if (nouvelle.AffilieId == Guid.Empty)
        {
            throw new DomainException("L'affilié de l'occupation est obligatoire.");
        }

        if (nouvelle.TypeTravailleur == TypeTravailleur.Interimaire)
        {
            if (nouvelle.AffilieUtilisateurId is null || nouvelle.AffilieUtilisateurId == Guid.Empty)
            {
                throw new DomainException("Un intérimaire doit être rattaché à l'affilié utilisateur (entreprise utilisatrice).");
            }

            if (nouvelle.AffilieUtilisateurId == nouvelle.AffilieId)
            {
                throw new DomainException("L'affilié utilisateur d'un intérimaire doit être distinct de l'agence d'intérim.");
            }
        }
        else if (nouvelle.AffilieUtilisateurId is not null)
        {
            throw new DomainException("Seul un intérimaire a un affilié utilisateur.");
        }

        if (nouvelle.DateFin is { } fin && fin < nouvelle.DateDebut)
        {
            throw new DomainException("La date de fin de l'occupation précède sa date de début.");
        }

        string? reference = null;
        if (!string.IsNullOrWhiteSpace(nouvelle.ReferenceDimona))
        {
            reference = nouvelle.ReferenceDimona.Trim().ToUpperInvariant();
            if (reference.Length > 50 || !reference.All(char.IsAsciiLetterOrDigit))
            {
                throw new DomainException("La référence DIMONA est alphanumérique (50 caractères maximum).");
            }
        }

        return new Occupation(id, nouvelle, reference);
    }

    internal bool Chevauche(Occupation autre) =>
        (autre.DateFin is null || DateDebut <= autre.DateFin) && (DateFin is null || autre.DateDebut <= DateFin);

    internal void Terminer(DateOnly dateFin)
    {
        if (DateFin is { } actuelle)
        {
            throw new DomainException($"L'occupation est déjà terminée au {actuelle:yyyy-MM-dd}.");
        }

        if (dateFin < DateDebut)
        {
            throw new DomainException($"La date de fin précède le début de l'occupation ({DateDebut:yyyy-MM-dd}).");
        }

        if (_affectations.Any(a => a.Validite.ValidFrom > dateFin))
        {
            throw new DomainException("Des affectations commencent après la date de fin : les clôturer ou les corriger d'abord.");
        }

        DateFin = dateFin;
    }

    internal Affectation Affecter(Guid id, Guid posteId, Guid siteId, DateOnly valideDu, DateOnly? valideJusquAu)
    {
        if (posteId == Guid.Empty || siteId == Guid.Empty)
        {
            throw new DomainException("Le poste et le site de l'affectation sont obligatoires.");
        }

        if (valideDu < DateDebut || (DateFin is { } fin && valideDu > fin))
        {
            throw new DomainException("L'affectation doit commencer pendant l'occupation.");
        }

        // Une affectation ne survit pas à l'occupation : borne exclusive au lendemain du dernier jour.
        var borne = DateFin?.AddDays(1);
        if (borne is { } b && (valideJusquAu is null || valideJusquAu > b))
        {
            valideJusquAu = b;
        }

        var validite = new Validity(valideDu, valideJusquAu);
        if (_affectations.Any(a => a.PosteId == posteId && a.Validite.Overlaps(validite)))
        {
            throw new DomainException("Le travailleur est déjà affecté à ce poste sur cette période.");
        }

        var affectation = new Affectation(id, posteId, siteId, validite);
        _affectations.Add(affectation);
        return affectation;
    }
}

/// <summary>
/// AFF-22 : rattachement à un poste d'un site, historisé par période de validité (DAT-04) :
/// une affectation se clôture, elle ne se modifie pas. Base du calcul des risques et obligations.
/// </summary>
public sealed class Affectation : Entity
{
    private Affectation()
    {
    }

    internal Affectation(Guid id, Guid posteId, Guid siteId, Validity validite) : base(id)
    {
        PosteId = posteId;
        SiteId = siteId;
        Validite = validite;
    }

    /// <summary>Référence au poste du service Postes et risques (DAT-02, sans clé étrangère physique).</summary>
    public Guid PosteId { get; private set; }

    /// <summary>Référence au site du service Affiliés.</summary>
    public Guid SiteId { get; private set; }

    public Validity Validite { get; private set; }

    internal void Cloturer(DateOnly fin) => Validite = Validite.CloseAt(fin);
}
