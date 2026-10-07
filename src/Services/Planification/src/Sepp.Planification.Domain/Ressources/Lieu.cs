using Sepp.BuildingBlocks.Domain;

namespace Sepp.Planification.Domain.Ressources;

/// <summary>PLA-02 : nature d'un lieu de prestation.</summary>
public enum TypeLieu
{
    /// <summary>Centre médical fixe du SEPP.</summary>
    CentreFixe,

    /// <summary>Cabinet médical installé chez un affilié.</summary>
    CabinetEntreprise,

    /// <summary>Unité mobile (car) stationnée chez l'affilié le temps d'une session.</summary>
    UniteMobile,

    /// <summary>Consultation à distance (téléconsultation, pour les actes qui le permettent).</summary>
    Distance,
}

/// <summary>
/// Lieu de prestation (PLA-02, §15.3 : id, type, adresse, site_id). Adresse professionnelle, jamais celle d'un travailleur.
/// Le code postal sert de zone géographique pour la planification automatique (PLA-04).
/// </summary>
public sealed class Lieu : AggregateRoot
{
    private Lieu()
    {
    }

    private Lieu(Guid id) : base(id)
    {
    }

    public TypeLieu Type { get; private set; }

    public string Nom { get; private set; } = string.Empty;

    public string? Adresse { get; private set; }

    public string? CodePostal { get; private set; }

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    /// <summary>Affilié chez qui se trouve le lieu (cabinet en entreprise, emplacement habituel de l'unité mobile).</summary>
    public Guid? AffilieId { get; private set; }

    /// <summary>Site physique de l'affilié (AFF-02) auquel le lieu correspond.</summary>
    public Guid? SiteId { get; private set; }

    public bool Actif { get; private set; } = true;

    public Coordonnees? Position => Latitude is { } lat && Longitude is { } lon ? new Coordonnees(lat, lon) : null;

    public static Lieu Creer(TypeLieu type, string nom, string? adresse, string? codePostal, Coordonnees? position, Guid? affilieId, Guid? siteId)
    {
        var lieu = new Lieu(NewId());
        lieu.Definir(type, nom, adresse, codePostal, position, affilieId, siteId);
        return lieu;
    }

    public void Modifier(string nom, string? adresse, string? codePostal, Coordonnees? position, Guid? affilieId, Guid? siteId) =>
        Definir(Type, nom, adresse, codePostal, position, affilieId, siteId);

    public void Desactiver() => Actif = false;

    public bool EstDansZone(string prefixeCodePostal) =>
        CodePostal is not null && CodePostal.StartsWith(prefixeCodePostal.Trim(), StringComparison.Ordinal);

    private void Definir(TypeLieu type, string nom, string? adresse, string? codePostal, Coordonnees? position, Guid? affilieId, Guid? siteId)
    {
        if (string.IsNullOrWhiteSpace(nom) || nom.Trim().Length > 200)
        {
            throw new DomainException("Le nom du lieu est obligatoire (200 caractères au plus).");
        }

        if (type == TypeLieu.CabinetEntreprise && affilieId is null)
        {
            throw new DomainException("Un cabinet en entreprise est rattaché à un affilié.");
        }

        if (type == TypeLieu.Distance && (adresse is not null || position is not null))
        {
            throw new DomainException("Une consultation à distance n'a ni adresse ni position.");
        }

        var cp = string.IsNullOrWhiteSpace(codePostal) ? null : codePostal.Trim();
        if (cp is not null && (cp.Length > 10 || !cp.All(char.IsAsciiLetterOrDigit)))
        {
            throw new DomainException($"Code postal invalide : '{codePostal}'.");
        }

        Type = type;
        Nom = nom.Trim();
        Adresse = string.IsNullOrWhiteSpace(adresse) ? null : adresse.Trim()[..Math.Min(adresse.Trim().Length, 300)];
        CodePostal = cp;
        Latitude = position?.Latitude;
        Longitude = position?.Longitude;
        AffilieId = affilieId;
        SiteId = siteId;
    }
}
