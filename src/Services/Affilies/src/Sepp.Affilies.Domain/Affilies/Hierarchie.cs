using Sepp.BuildingBlocks.Domain;

namespace Sepp.Affilies.Domain.Affilies;

/// <summary>
/// AFF-02 — Hiérarchie de l'affilié : entité juridique (l'affilié) → unités d'établissement (BCE) → sites physiques
/// → départements (hiérarchie libre). Les éléments ne sont jamais supprimés mais clôturés (DAT-04), les travailleurs
/// pouvant y rester rattachés dans l'historique.
/// </summary>
public sealed partial class Affilie
{
    public UniteEtablissement AjouterUniteEtablissement(NumeroUniteEtablissement numero, string nom, Adresse adresse, Language langue, DateOnly depuis)
    {
        VerifierModifiable();
        if (_unitesEtablissement.Any(u => u.Numero == numero))
        {
            throw new DomainException($"L'unité d'établissement {numero} est déjà rattachée à l'affilié.");
        }

        var unite = new UniteEtablissement(NewId(), numero, Texte.Obligatoire(nom, "Le nom de l'unité d'établissement", 200), adresse, langue, new Validity(depuis));
        _unitesEtablissement.Add(unite);
        IncrementerVersion();
        return unite;
    }

    public void ModifierUniteEtablissement(Guid uniteId, string nom, Adresse adresse, Language langue)
    {
        VerifierModifiable();
        UniteOuverte(uniteId).Modifier(Texte.Obligatoire(nom, "Le nom de l'unité d'établissement", 200), adresse, langue);
        IncrementerVersion();
    }

    /// <summary>Ferme une unité d'établissement (fin exclusive) ainsi que ses sites et départements encore ouverts.</summary>
    public void FermerUniteEtablissement(Guid uniteId, DateOnly fin)
    {
        VerifierModifiable();
        var unite = UniteOuverte(uniteId);
        var validite = Cloturer(unite.Validite, fin, "L'unité d'établissement");
        foreach (var site in unite.Sites.Where(s => s.Validite.IsOpen))
        {
            site.Fermer(fin);
        }

        unite.Fermer(validite);
        IncrementerVersion();
    }

    public Site AjouterSite(Guid uniteId, string nom, Adresse adresse, double? latitude, double? longitude, DateOnly depuis)
    {
        VerifierModifiable();
        var unite = UniteOuverte(uniteId);
        var site = new Site(NewId(), Texte.Obligatoire(nom, "Le nom du site", 200), adresse, Periode(depuis, "Le site", unite.Validite));
        site.Localiser(latitude, longitude);
        unite.Ajouter(site);
        IncrementerVersion();
        return site;
    }

    public void ModifierSite(Guid siteId, string nom, Adresse adresse, double? latitude, double? longitude)
    {
        VerifierModifiable();
        var site = SiteOuvert(siteId);
        site.Modifier(Texte.Obligatoire(nom, "Le nom du site", 200), adresse);
        site.Localiser(latitude, longitude);
        IncrementerVersion();
    }

    /// <summary>Ferme un site (fin exclusive) et ses départements encore ouverts.</summary>
    public void FermerSite(Guid siteId, DateOnly fin)
    {
        VerifierModifiable();
        var site = SiteOuvert(siteId);
        Cloturer(site.Validite, fin, "Le site");
        site.Fermer(fin);
        IncrementerVersion();
    }

    /// <summary>Ajoute un département à un site, à la racine ou sous un département parent du même site.</summary>
    public Departement AjouterDepartement(Guid siteId, string nom, Guid? parentId, DateOnly depuis)
    {
        VerifierModifiable();
        var site = SiteOuvert(siteId);
        var validite = Periode(depuis, "Le département", site.Validite);
        if (parentId is { } parent)
        {
            var departementParent = DepartementOuvertDansSite(site, parent);
            validite = Periode(depuis, "Le département", departementParent.Validite);
        }

        var departement = new Departement(NewId(), Texte.Obligatoire(nom, "Le nom du département", 200), parentId, validite);
        site.Ajouter(departement);
        IncrementerVersion();
        return departement;
    }

    /// <summary>Renomme ou déplace un département dans la hiérarchie de son site, sans créer de cycle.</summary>
    public void ModifierDepartement(Guid departementId, string nom, Guid? parentId)
    {
        VerifierModifiable();
        var (site, departement) = DepartementOuvert(departementId);
        if (parentId is { } parent)
        {
            DepartementOuvertDansSite(site, parent);
            for (Guid? ancetre = parent; ancetre is { } a; ancetre = site.Departements.Single(d => d.Id == a).ParentId)
            {
                if (a == departementId)
                {
                    throw new DomainException("Un département ne peut pas être rattaché à lui-même ni à l'un de ses sous-départements.");
                }
            }
        }

        departement.Modifier(Texte.Obligatoire(nom, "Le nom du département", 200), parentId);
        IncrementerVersion();
    }

    /// <summary>Ferme un département (fin exclusive) et ses sous-départements encore ouverts.</summary>
    public void FermerDepartement(Guid departementId, DateOnly fin)
    {
        VerifierModifiable();
        var (site, departement) = DepartementOuvert(departementId);
        Cloturer(departement.Validite, fin, "Le département");
        site.FermerBranche(departement, fin);
        IncrementerVersion();
    }

    private UniteEtablissement UniteOuverte(Guid uniteId)
    {
        var unite = _unitesEtablissement.SingleOrDefault(u => u.Id == uniteId)
                    ?? throw new ElementIntrouvableException($"Unité d'établissement {uniteId} inconnue pour cet affilié.");
        return unite.Validite.IsOpen ? unite : throw new DomainException($"L'unité d'établissement {unite.Numero} est fermée.");
    }

    private Site SiteOuvert(Guid siteId)
    {
        var site = _unitesEtablissement.SelectMany(u => u.Sites).SingleOrDefault(s => s.Id == siteId)
                   ?? throw new ElementIntrouvableException($"Site {siteId} inconnu pour cet affilié.");
        return site.Validite.IsOpen ? site : throw new DomainException($"Le site « {site.Nom} » est fermé.");
    }

    private (Site Site, Departement Departement) DepartementOuvert(Guid departementId)
    {
        var site = _unitesEtablissement.SelectMany(u => u.Sites).SingleOrDefault(s => s.Departements.Any(d => d.Id == departementId))
                   ?? throw new ElementIntrouvableException($"Département {departementId} inconnu pour cet affilié.");
        return (site, DepartementOuvertDansSite(site, departementId));
    }

    private static Departement DepartementOuvertDansSite(Site site, Guid departementId)
    {
        var departement = site.Departements.SingleOrDefault(d => d.Id == departementId)
                          ?? throw new DomainException($"Le département {departementId} n'appartient pas au site « {site.Nom} ».");
        return departement.Validite.IsOpen ? departement : throw new DomainException($"Le département « {departement.Nom} » est fermé.");
    }
}

/// <summary>Unité d'établissement : lieu d'activité identifié à la BCE (AFF-02).</summary>
public sealed class UniteEtablissement : Entity
{
    private readonly List<Site> _sites = [];

    private UniteEtablissement()
    {
    }

    internal UniteEtablissement(Guid id, NumeroUniteEtablissement numero, string nom, Adresse adresse, Language langue, Validity validite) : base(id)
    {
        Numero = numero;
        Nom = nom;
        Adresse = adresse;
        Langue = langue;
        Validite = validite;
    }

    public NumeroUniteEtablissement Numero { get; private set; } = null!;

    public string Nom { get; private set; } = string.Empty;

    public Adresse Adresse { get; private set; } = null!;

    public Language Langue { get; private set; }

    public Validity Validite { get; private set; }

    public IReadOnlyList<Site> Sites => _sites.AsReadOnly();

    internal void Modifier(string nom, Adresse adresse, Language langue)
    {
        Nom = nom;
        Adresse = adresse;
        Langue = langue;
    }

    internal void Ajouter(Site site) => _sites.Add(site);

    internal void Fermer(Validity validite) => Validite = validite;
}

/// <summary>Site physique de travail, géolocalisé pour l'organisation des tournées (AFF-02).</summary>
public sealed class Site : Entity
{
    private readonly List<Departement> _departements = [];

    private Site()
    {
    }

    internal Site(Guid id, string nom, Adresse adresse, Validity validite) : base(id)
    {
        Nom = nom;
        Adresse = adresse;
        Validite = validite;
    }

    public string Nom { get; private set; } = string.Empty;

    public Adresse Adresse { get; private set; } = null!;

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    public Validity Validite { get; private set; }

    public IReadOnlyList<Departement> Departements => _departements.AsReadOnly();

    internal void Modifier(string nom, Adresse adresse)
    {
        Nom = nom;
        Adresse = adresse;
    }

    internal void Localiser(double? latitude, double? longitude)
    {
        if (latitude.HasValue != longitude.HasValue)
        {
            throw new DomainException("La latitude et la longitude se renseignent ensemble.");
        }

        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            throw new DomainException("Coordonnées géographiques hors limites (latitude −90 à 90, longitude −180 à 180).");
        }

        Latitude = latitude;
        Longitude = longitude;
    }

    internal void Ajouter(Departement departement) => _departements.Add(departement);

    internal void Fermer(DateOnly fin)
    {
        foreach (var departement in _departements.Where(d => d.Validite.IsOpen))
        {
            departement.Fermer(fin);
        }

        Validite = Validite.CloseAt(fin);
    }

    internal void FermerBranche(Departement racine, DateOnly fin)
    {
        foreach (var enfant in _departements.Where(d => d.ParentId == racine.Id && d.Validite.IsOpen).ToList())
        {
            FermerBranche(enfant, fin);
        }

        racine.Fermer(fin);
    }
}

/// <summary>Département d'un site ; hiérarchie libre par <see cref="ParentId"/> (AFF-02).</summary>
public sealed class Departement : Entity
{
    private Departement()
    {
    }

    internal Departement(Guid id, string nom, Guid? parentId, Validity validite) : base(id)
    {
        Nom = nom;
        ParentId = parentId;
        Validite = validite;
    }

    public string Nom { get; private set; } = string.Empty;

    public Guid? ParentId { get; private set; }

    public Validity Validite { get; private set; }

    internal void Modifier(string nom, Guid? parentId)
    {
        Nom = nom;
        ParentId = parentId;
    }

    internal void Fermer(DateOnly fin) => Validite = Validite.CloseAt(fin);
}

/// <summary>Élément (unité, site, contact…) inexistant dans l'agrégat : traduit en « introuvable » par l'application.</summary>
public sealed class ElementIntrouvableException : DomainException
{
    public ElementIntrouvableException(string message) : base(message)
    {
    }

    public ElementIntrouvableException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public ElementIntrouvableException()
    {
    }
}
