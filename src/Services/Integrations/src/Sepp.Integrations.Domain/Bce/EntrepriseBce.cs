using Sepp.BuildingBlocks.Domain;

namespace Sepp.Integrations.Domain.Bce;

/// <summary>Adresse d'une unité d'établissement telle que reçue de la BCE (donnée d'entreprise).</summary>
public sealed record AdresseBce
{
    public AdresseBce(string rue, string numero, string? boite, string codePostal, string localite, string codePays = "BE")
    {
        Rue = Texte.Obligatoire(rue, "La rue", 200);
        Numero = Texte.Obligatoire(numero, "Le numéro", 20);
        Boite = Texte.Facultatif(boite, "La boîte", 20);
        CodePostal = Texte.Obligatoire(codePostal, "Le code postal", 20);
        Localite = Texte.Obligatoire(localite, "La localité", 100);
        var pays = Texte.Obligatoire(codePays, "Le code pays", 2).ToUpperInvariant();
        CodePays = pays.Length == 2 && pays.All(char.IsAsciiLetterUpper)
            ? pays
            : throw new DomainException($"Le code pays '{codePays}' doit être un code ISO 3166-1 alpha-2.");
    }

    public string Rue { get; }

    public string Numero { get; }

    public string? Boite { get; }

    public string CodePostal { get; }

    public string Localite { get; }

    public string CodePays { get; }
}

/// <summary>Unité d'établissement reçue de la BCE (format canonique interne, indépendant du format de l'organisme).</summary>
public sealed record DonneesUniteEtablissement(string Numero, string Denomination, AdresseBce Adresse, DateOnly? DateDebut);

/// <summary>Données d'une entreprise reçues de la BCE (format canonique interne).</summary>
public sealed record DonneesEntreprise(
    string NumeroBce,
    string Denomination,
    string FormeJuridique,
    string CodeNace,
    DateOnly DateExtraction,
    IReadOnlyList<DonneesUniteEtablissement> UnitesEtablissement);

/// <summary>
/// Dernières données connues d'une entreprise à la BCE (§12) : dénomination, forme juridique, code NACE-BEL et unités
/// d'établissement avec leur adresse. Sert de référence pour détecter les changements et pour la lecture par le
/// service Affiliés (les adresses ne voyagent pas dans l'événement, ARC-06).
/// </summary>
public sealed class EntrepriseBce : AggregateRoot
{
    private readonly List<UniteEtablissementBce> _unitesEtablissement = [];

    private EntrepriseBce()
    {
    }

    private EntrepriseBce(Guid id, string numeroBce) : base(id)
    {
        NumeroBce = numeroBce;
    }

    public string NumeroBce { get; private set; } = string.Empty;

    public string Denomination { get; private set; } = string.Empty;

    public string FormeJuridique { get; private set; } = string.Empty;

    public string CodeNace { get; private set; } = string.Empty;

    public DateOnly DateExtraction { get; private set; }

    public DateTimeOffset ActualiseeLe { get; private set; }

    public IReadOnlyList<UniteEtablissementBce> UnitesEtablissement => _unitesEtablissement.AsReadOnly();

    public static EntrepriseBce Creer(DonneesEntreprise donnees, DateTimeOffset maintenant)
    {
        var entreprise = new EntrepriseBce(NewId(), NumerosBce.Entreprise(donnees.NumeroBce));
        entreprise.Actualiser(donnees, maintenant);
        return entreprise;
    }

    /// <summary>Applique les données reçues ; renvoie <c>true</c> si quelque chose a changé (sinon aucun effet).</summary>
    public bool Actualiser(DonneesEntreprise donnees, DateTimeOffset maintenant)
    {
        if (NumerosBce.Entreprise(donnees.NumeroBce) != NumeroBce)
        {
            throw new DomainException("Les données reçues concernent une autre entreprise.");
        }

        var denomination = Texte.Obligatoire(donnees.Denomination, "La dénomination", 200);
        var forme = Texte.Obligatoire(donnees.FormeJuridique, "La forme juridique", 50);
        var nace = Texte.Obligatoire(donnees.CodeNace, "Le code NACE", 10);
        var unites = (donnees.UnitesEtablissement ?? []).Select(u => new
        {
            Numero = NumerosBce.UniteEtablissement(u.Numero),
            Denomination = Texte.Obligatoire(u.Denomination, "La dénomination de l'unité d'établissement", 200),
            Adresse = u.Adresse ?? throw new DomainException("L'adresse de l'unité d'établissement est obligatoire."),
            u.DateDebut,
        }).ToList();
        if (unites.Select(u => u.Numero).Distinct(StringComparer.Ordinal).Count() != unites.Count)
        {
            throw new DomainException("Une unité d'établissement figure deux fois dans les données reçues.");
        }

        var change = denomination != Denomination || forme != FormeJuridique || nace != CodeNace;
        Denomination = denomination;
        FormeJuridique = forme;
        CodeNace = nace;

        foreach (var disparue in _unitesEtablissement.Where(u => unites.All(n => n.Numero != u.Numero)).ToList())
        {
            _unitesEtablissement.Remove(disparue);
            change = true;
        }

        foreach (var recue in unites)
        {
            var existante = _unitesEtablissement.SingleOrDefault(u => u.Numero == recue.Numero);
            if (existante is null)
            {
                _unitesEtablissement.Add(new UniteEtablissementBce(NewId(), recue.Numero, recue.Denomination, recue.Adresse, recue.DateDebut));
                change = true;
            }
            else
            {
                change |= existante.Modifier(recue.Denomination, recue.Adresse, recue.DateDebut);
            }
        }

        if (donnees.DateExtraction > DateExtraction)
        {
            DateExtraction = donnees.DateExtraction;
        }

        if (change)
        {
            ActualiseeLe = maintenant;
            Raise(new EntrepriseBceActualisee(Id, NumeroBce, maintenant));
        }

        return change;
    }
}

public sealed class UniteEtablissementBce : Entity
{
    private UniteEtablissementBce()
    {
    }

    internal UniteEtablissementBce(Guid id, string numero, string denomination, AdresseBce adresse, DateOnly? dateDebut) : base(id)
    {
        Numero = numero;
        Denomination = denomination;
        Adresse = adresse;
        DateDebut = dateDebut;
    }

    public string Numero { get; private set; } = string.Empty;

    public string Denomination { get; private set; } = string.Empty;

    public AdresseBce Adresse { get; private set; } = null!;

    public DateOnly? DateDebut { get; private set; }

    internal bool Modifier(string denomination, AdresseBce adresse, DateOnly? dateDebut)
    {
        if (denomination == Denomination && adresse == Adresse && dateDebut == DateDebut)
        {
            return false;
        }

        Denomination = denomination;
        Adresse = adresse;
        DateDebut = dateDebut;
        return true;
    }
}

/// <summary>Les données BCE d'une entreprise ont changé.</summary>
public sealed record EntrepriseBceActualisee(Guid EntrepriseId, string NumeroBce, DateTimeOffset OccurredAt) : IDomainEvent;
