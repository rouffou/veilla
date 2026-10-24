using Sepp.BuildingBlocks.Domain;

namespace Sepp.Documents.Domain.Langues;

/// <summary>Objet dont la projection linguistique retient la langue : un affilié ou une personne.</summary>
public enum SujetLinguistique
{
    Affilie,
    Personne,
}

/// <summary>
/// NF-41, ADR 0009 : modèle de lecture local du contexte linguistique (table <c>contexte_linguistique</c>), alimenté par les
/// événements d'Affiliés (régime linguistique et langue de l'affilié) et de Personnes (langue du travailleur). Il permet à
/// l'instance médicale, qui ne peut appeler ni Affiliés ni Personnes, de choisir la langue des documents. Ni identité ni
/// donnée de santé : un identifiant, une langue, un régime et l'horodatage de l'événement source (ARC-06).
/// Idempotent : clé = (sujet, identifiant) ; un événement plus ancien que l'état connu est ignoré.
/// </summary>
public sealed class ContexteLinguistiqueProjete
{
    private ContexteLinguistiqueProjete()
    {
    }

    private ContexteLinguistiqueProjete(SujetLinguistique sujet, Guid sujetId, RegimeLinguistique? regime, Language? langue, DateTimeOffset evenementDu)
    {
        Sujet = sujet;
        SujetId = sujetId;
        Regime = regime;
        Langue = langue;
        EvenementDu = evenementDu;
    }

    public SujetLinguistique Sujet { get; private set; }

    /// <summary>Identifiant de l'affilié ou de la personne.</summary>
    public Guid SujetId { get; private set; }

    /// <summary>Régime linguistique de l'affilié ; toujours <c>null</c> pour une personne.</summary>
    public RegimeLinguistique? Regime { get; private set; }

    /// <summary>Langue de correspondance de l'affilié ou langue choisie par le travailleur.</summary>
    public Language? Langue { get; private set; }

    /// <summary>Horodatage (<c>OccurredAt</c>) de l'événement source le plus récent appliqué.</summary>
    public DateTimeOffset EvenementDu { get; private set; }

    public static ContexteLinguistiqueProjete Affilie(Guid affilieId, RegimeLinguistique? regime, Language? langue, DateTimeOffset evenementDu) =>
        new(SujetLinguistique.Affilie, affilieId, regime, langue, evenementDu);

    public static ContexteLinguistiqueProjete Personne(Guid personneId, Language langue, DateTimeOffset evenementDu) =>
        new(SujetLinguistique.Personne, personneId, null, langue, evenementDu);

    /// <summary>
    /// Applique l'état porté par un événement. Une valeur absente (producteur antérieur, valeur inconnue) ne remplace pas la
    /// valeur connue. À horodatage égal, l'événement reçu en dernier l'emporte.
    /// </summary>
    /// <returns><c>false</c> si l'événement est plus ancien que l'état connu (livraison désordonnée) : rien n'est modifié.</returns>
    public bool Appliquer(RegimeLinguistique? regime, Language? langue, DateTimeOffset evenementDu)
    {
        if (evenementDu < EvenementDu)
        {
            return false;
        }

        if (Sujet == SujetLinguistique.Affilie)
        {
            Regime = regime ?? Regime;
        }

        Langue = langue ?? Langue;
        EvenementDu = evenementDu;
        return true;
    }
}
