using Sepp.BuildingBlocks.Domain;

namespace Sepp.Affilies.Domain.Ecarts;

public enum StatutEcart
{
    /// <summary>À traiter par le gestionnaire de dossiers.</summary>
    Ouvert,

    /// <summary>Clos : disparu à une réception ultérieure des données BCE, ou marqué traité par le gestionnaire.</summary>
    Resolu,
}

/// <summary>
/// Écart entre la BCE et la fiche d'un affilié que la synchronisation ne sait pas résoudre automatiquement (AFF-01, AFF-02,
/// INT-04) : affilié inconnu, unité rattachée à un autre affilié, donnée invalide… Consigné au lieu de faire échouer le
/// message ; visible du gestionnaire de dossiers. Un même écart (numéro BCE, code, référence) n'existe qu'une fois tant qu'il est ouvert.
/// Ne porte que des données d'entreprise (numéros, dénominations), jamais de donnée personnelle (ARC-06).
/// </summary>
public sealed class EcartSynchronisation : Entity
{
    private EcartSynchronisation()
    {
    }

    private EcartSynchronisation(Guid id, string numeroBce, Guid? affilieId, string code, string reference, string detail, DateOnly dateExtraction, DateTimeOffset maintenant)
        : base(id)
    {
        NumeroBce = numeroBce;
        AffilieId = affilieId;
        Code = code;
        Reference = reference;
        Detail = detail;
        DateExtraction = dateExtraction;
        DetecteLe = maintenant;
        DerniereDetectionLe = maintenant;
        Statut = StatutEcart.Ouvert;
    }

    /// <summary>Numéro d'entreprise sous sa forme canonique à dix chiffres.</summary>
    public string NumeroBce { get; private set; } = string.Empty;

    /// <summary>Affilié concerné, <c>null</c> si le numéro BCE ne correspond à aucun affilié.</summary>
    public Guid? AffilieId { get; private set; }

    /// <summary>Code de l'écart (voir <c>CodesEcartBce</c>).</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Objet concerné (numéro d'unité d'établissement, numéro BCE…).</summary>
    public string Reference { get; private set; } = string.Empty;

    public string Detail { get; private set; } = string.Empty;

    /// <summary>Date d'extraction BCE des données qui ont produit l'écart.</summary>
    public DateOnly DateExtraction { get; private set; }

    public DateTimeOffset DetecteLe { get; private set; }

    public DateTimeOffset DerniereDetectionLe { get; private set; }

    public StatutEcart Statut { get; private set; }

    public DateTimeOffset? ResoluLe { get; private set; }

    /// <summary>Auteur de la résolution : identifiant de l'utilisateur ou « system » (disparu de lui-même).</summary>
    public string? ResoluPar { get; private set; }

    public static EcartSynchronisation Ouvrir(string numeroBce, Guid? affilieId, string code, string reference, string detail, DateOnly dateExtraction, DateTimeOffset maintenant)
    {
        if (string.IsNullOrWhiteSpace(numeroBce) || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(reference))
        {
            throw new DomainException("Un écart identifie le numéro BCE, le code et la référence.");
        }

        return new EcartSynchronisation(NewId(), numeroBce, affilieId, code, Tronquer(reference, 100), Tronquer(detail, 1000), dateExtraction, maintenant.ToUniversalTime());
    }

    /// <summary>Le même écart est constaté à nouveau : détail et date mis à jour, sans doublon.</summary>
    public void Redetecter(Guid? affilieId, string detail, DateOnly dateExtraction, DateTimeOffset maintenant)
    {
        AffilieId = affilieId;
        Detail = Tronquer(detail, 1000);
        DateExtraction = dateExtraction;
        DerniereDetectionLe = maintenant.ToUniversalTime();
    }

    public void Resoudre(string auteur, DateTimeOffset maintenant)
    {
        if (Statut == StatutEcart.Resolu)
        {
            return;
        }

        Statut = StatutEcart.Resolu;
        ResoluLe = maintenant.ToUniversalTime();
        ResoluPar = auteur;
    }

    private static string Tronquer(string texte, int longueurMax) =>
        texte.Length <= longueurMax ? texte : texte[..longueurMax];
}
