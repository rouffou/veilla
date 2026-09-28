using Sepp.BuildingBlocks.Domain;

namespace Sepp.Affilies.Domain.Affilies;

/// <summary>Données descriptives de la fiche employeur (AFF-01), validées et normalisées.</summary>
public sealed record DonneesFiche
{
    public DonneesFiche(
        string denomination,
        string formeJuridique,
        string codeNace,
        string commissionParitaire,
        CategorieTarifaire categorieTarifaire,
        Language langue,
        RegimeLinguistique regimeLinguistique)
    {
        Denomination = Texte.Obligatoire(denomination, "La dénomination", 200);
        FormeJuridique = Texte.Obligatoire(formeJuridique, "La forme juridique", 50);
        CodeNace = Texte.CodeNace(codeNace);
        CommissionParitaire = Texte.CommissionParitaire(commissionParitaire);
        if (!Enum.IsDefined(categorieTarifaire))
        {
            throw new DomainException($"Catégorie tarifaire inconnue : {categorieTarifaire} (attendu : A à D).");
        }

        if (!Enum.IsDefined(langue) || !Enum.IsDefined(regimeLinguistique))
        {
            throw new DomainException("Langue ou régime linguistique inconnu.");
        }

        CategorieTarifaire = categorieTarifaire;
        Langue = langue;
        RegimeLinguistique = regimeLinguistique;
    }

    public string Denomination { get; }

    /// <summary>Forme juridique (SRL, SA, ASBL…) ou son code BCE.</summary>
    public string FormeJuridique { get; }

    /// <summary>Code NACE-BEL de l'activité principale (nomenclature du service Référentiels, DAT-02).</summary>
    public string CodeNace { get; }

    public string CommissionParitaire { get; }

    public CategorieTarifaire CategorieTarifaire { get; }

    /// <summary>Langue de correspondance.</summary>
    public Language Langue { get; }

    public RegimeLinguistique RegimeLinguistique { get; }
}

/// <summary>
/// Affilié : entité juridique employeur (numéro BCE) affiliée au SEPP. Racine d'agrégat du service (§15.3) :
/// fiche (AFF-01), hiérarchie (AFF-02), contacts (AFF-03), organes de concertation (AFF-04)
/// et opérations de fusion, scission et transfert (AFF-06). Chaque modification incrémente
/// le numéro de version de la fiche, repris dans l'historique (AFF-05).
/// </summary>
public sealed partial class Affilie : AggregateRoot
{
    private readonly List<UniteEtablissement> _unitesEtablissement = [];
    private readonly List<Contact> _contacts = [];
    private readonly List<OrganeConcertation> _organesConcertation = [];
    private readonly List<OperationAffilie> _operations = [];

    private Affilie()
    {
    }

    private Affilie(Guid id, NumeroBce numeroBce, DonneesFiche fiche, DateOnly dateAffiliation, Guid? groupeId) : base(id)
    {
        NumeroBce = numeroBce;
        DateAffiliation = dateAffiliation;
        Statut = StatutAffilie.Actif;
        GroupeId = groupeId;
        Appliquer(fiche);
    }

    public NumeroBce NumeroBce { get; private set; } = null!;

    public string Denomination { get; private set; } = string.Empty;

    public string FormeJuridique { get; private set; } = string.Empty;

    public string CodeNace { get; private set; } = string.Empty;

    public string CommissionParitaire { get; private set; } = string.Empty;

    public CategorieTarifaire CategorieTarifaire { get; private set; }

    public DateOnly DateAffiliation { get; private set; }

    /// <summary>Dernier jour couvert par l'affiliation (inclus) ; <c>null</c> tant qu'elle est en cours.</summary>
    public DateOnly? DateFin { get; private set; }

    public Language Langue { get; private set; }

    public RegimeLinguistique RegimeLinguistique { get; private set; }

    public StatutAffilie Statut { get; private set; }

    /// <summary>Groupe d'appartenance facultatif (AFF-02).</summary>
    public Guid? GroupeId { get; private set; }

    /// <summary>Version de la fiche, incrémentée à chaque modification (AFF-05).</summary>
    public int NumeroVersion { get; private set; }

    public IReadOnlyList<UniteEtablissement> UnitesEtablissement => _unitesEtablissement.AsReadOnly();

    public IReadOnlyList<Contact> Contacts => _contacts.AsReadOnly();

    public IReadOnlyList<OrganeConcertation> OrganesConcertation => _organesConcertation.AsReadOnly();

    public IReadOnlyList<OperationAffilie> Operations => _operations.AsReadOnly();

    /// <summary>
    /// Affilie un employeur. S'il provient d'un autre SEPP, le transfert entrant est enregistré comme opération
    /// réalisée à la date d'affiliation (AFF-06).
    /// </summary>
    public static Affilie Creer(NumeroBce numeroBce, DonneesFiche fiche, DateOnly dateAffiliation, Guid? groupeId = null, string? seppOrigine = null)
    {
        var affilie = new Affilie(NewId(), numeroBce, fiche, dateAffiliation, groupeId);
        if (seppOrigine is not null)
        {
            affilie._operations.Add(OperationAffilie.TransfertEntrant(NewId(), Texte.Obligatoire(seppOrigine, "Le SEPP d'origine", 200), dateAffiliation));
        }

        affilie.IncrementerVersion();
        return affilie;
    }

    /// <summary>Affiliation en cours à une date donnée.</summary>
    public bool EstAffilieAu(DateOnly date) => date >= DateAffiliation && (DateFin is null || date <= DateFin);

    /// <summary>Modifie la fiche employeur (AFF-01) et le groupe d'appartenance (AFF-02).</summary>
    public void ModifierFiche(DonneesFiche fiche, Guid? groupeId)
    {
        VerifierModifiable();
        Appliquer(fiche);
        GroupeId = groupeId;
        IncrementerVersion();
    }

    /// <summary>Résilie l'affiliation : <paramref name="dateFin"/> est le dernier jour couvert.</summary>
    public void Resilier(DateOnly dateFin)
    {
        VerifierModifiable();
        if (dateFin < DateAffiliation)
        {
            throw new DomainException($"La date de fin ({dateFin:yyyy-MM-dd}) ne peut pas précéder la date d'affiliation ({DateAffiliation:yyyy-MM-dd}).");
        }

        if (_operations.Any(o => o.Statut == StatutOperation.Projetee))
        {
            throw new DomainException("Une opération de fusion, scission ou transfert est en cours : l'annuler ou la réaliser d'abord.");
        }

        DateFin = dateFin;
        Statut = StatutAffilie.Resilie;
        IncrementerVersion();
    }

    /// <summary>Annule une résiliation (révocation du préavis) : l'affiliation redevient à durée indéterminée.</summary>
    public void AnnulerResiliation()
    {
        if (Statut != StatutAffilie.Resilie)
        {
            throw new DomainException("Seule une affiliation résiliée peut être rétablie.");
        }

        DateFin = null;
        Statut = StatutAffilie.Actif;
        IncrementerVersion();
    }

    private void Appliquer(DonneesFiche fiche)
    {
        Denomination = fiche.Denomination;
        FormeJuridique = fiche.FormeJuridique;
        CodeNace = fiche.CodeNace;
        CommissionParitaire = fiche.CommissionParitaire;
        CategorieTarifaire = fiche.CategorieTarifaire;
        Langue = fiche.Langue;
        RegimeLinguistique = fiche.RegimeLinguistique;
    }

    /// <summary>Un affilié absorbé, scindé ou transféré n'est plus modifiable : sa fiche est figée pour l'historique.</summary>
    private void VerifierModifiable()
    {
        if (Statut is StatutAffilie.Absorbe or StatutAffilie.Scinde or StatutAffilie.Transfere)
        {
            throw new DomainException($"L'affilié {NumeroBce} est clôturé (statut {Statut}) et ne peut plus être modifié.");
        }
    }

    private void IncrementerVersion()
    {
        NumeroVersion++;
        Raise(new AffilieVersionne(Id, NumeroVersion, DateTimeOffset.UtcNow));
    }

    private static Validity Periode(DateOnly debut, string objet, Validity parent)
    {
        if (!parent.Contains(debut))
        {
            throw new DomainException($"{objet} doit débuter pendant la période de validité de son rattachement.");
        }

        return new Validity(debut);
    }

    private static Validity Cloturer(Validity validite, DateOnly fin, string objet)
    {
        if (!validite.IsOpen)
        {
            throw new DomainException($"{objet} est déjà clôturé(e).");
        }

        if (fin <= validite.ValidFrom)
        {
            throw new DomainException($"La fin de {objet.ToLowerInvariant()} doit être postérieure au {validite.ValidFrom:yyyy-MM-dd}.");
        }

        return validite.CloseAt(fin);
    }
}

/// <summary>La fiche d'un affilié a changé de version (AFF-05).</summary>
public sealed record AffilieVersionne(Guid AffilieId, int NumeroVersion, DateTimeOffset OccurredAt) : IDomainEvent;
