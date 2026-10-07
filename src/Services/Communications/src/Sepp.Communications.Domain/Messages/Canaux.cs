using Sepp.BuildingBlocks.Domain;

namespace Sepp.Communications.Domain.Messages;

/// <summary>Canal d'envoi (DOC-03) : un port et un simulateur par canal.</summary>
public enum Canal
{
    /// <summary>Notification dans l'espace sécurisé (portails travailleur, employeur, interne).</summary>
    Portail,

    /// <summary>E-mail : notification générique et lien, jamais de donnée sensible (DOC-03).</summary>
    Email,

    /// <summary>SMS : notification générique et lien.</summary>
    Sms,

    /// <summary>Courrier postal, éventuellement recommandé (<see cref="Message.Recommande"/>).</summary>
    Courrier,

    /// <summary>Recommandé électronique qualifié (SAN-11).</summary>
    RecommandeElectronique,

    /// <summary>eBox Entreprise (messagerie sécurisée des entreprises).</summary>
    EBoxEntreprise,

    /// <summary>eBox Citoyen (messagerie sécurisée des citoyens).</summary>
    EBoxCitoyen,
}

public static class Canaux
{
    /// <summary>
    /// Canaux qui peuvent porter un contenu détaillé : l'accès au message passe par une authentification (portail, eBox) ou
    /// par un envoi postal nominatif. L'e-mail et le SMS ne portent jamais que la notification générique (DOC-03).
    /// </summary>
    public static bool PeutPorterUnContenuDetaille(this Canal canal) =>
        canal is not (Canal.Email or Canal.Sms);

    /// <summary>Interprète le canal indiqué par un événement ou une préférence ; <c>null</c> si inconnu.</summary>
    public static Canal? Depuis(string? valeur) => valeur?.Trim().Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant() switch
    {
        "PORTAIL" => Canal.Portail,
        "EMAIL" or "COURRIEL" or "MAIL" => Canal.Email,
        "SMS" => Canal.Sms,
        "COURRIER" or "POSTE" => Canal.Courrier,
        "RECOMMANDEELECTRONIQUE" or "RECOMMANDE" => Canal.RecommandeElectronique,
        "EBOXENTREPRISE" => Canal.EBoxEntreprise,
        "EBOXCITOYEN" => Canal.EBoxCitoyen,
        _ => null,
    };
}

/// <summary>Nature du message (§15.3 <c>message</c>).</summary>
public enum TypeMessage
{
    /// <summary>Un document est disponible (<c>documents.document-publie</c>).</summary>
    NotificationDocument,

    /// <summary>Convocation à un rendez-vous (SAN-10, SAN-11).</summary>
    ConvocationRendezVous,

    /// <summary>Rappel de rendez-vous (SAN-13).</summary>
    RappelRendezVous,

    /// <summary>Annulation d'un rendez-vous.</summary>
    AnnulationRendezVous,

    /// <summary>Alerte aux dirigeants après un accès « bris de glace » (§3.3).</summary>
    AlerteBrisDeGlace,

    /// <summary>Message rédigé par un gestionnaire (canaux sécurisés uniquement).</summary>
    Manuel,
}

public enum TypeDestinataire
{
    Affilie,
    Personne,

    /// <summary>Utilisateur interne du SEPP (dirigeants alertés d'un bris de glace).</summary>
    Interne,
}

/// <summary>
/// Cycle de vie d'un message : en attente → envoyé, ou en échec (nouvelle tentative programmée) puis abandonné après
/// l'épuisement des reprises ; un message non encore envoyé peut être annulé.
/// </summary>
public enum StatutMessage
{
    EnAttente,
    EnEchec,
    Envoye,
    Abandonne,
    Annule,
}

/// <summary>Canaux joignables pour un destinataire, déduits de ses coordonnées (sans conserver les coordonnées).</summary>
public sealed record Joignabilite(bool Portail, bool Email, bool Sms, bool Courrier, bool EBoxEntreprise, bool EBoxCitoyen)
{
    public bool Peut(Canal canal) => canal switch
    {
        Canal.Portail => Portail,
        Canal.Email => Email,
        Canal.Sms => Sms,
        Canal.Courrier => Courrier,
        Canal.RecommandeElectronique => Email || EBoxEntreprise || EBoxCitoyen,
        Canal.EBoxEntreprise => EBoxEntreprise,
        Canal.EBoxCitoyen => EBoxCitoyen,
        _ => false,
    };
}

/// <summary>Canal retenu pour un envoi.</summary>
public sealed record CanalChoisi(Canal Canal, bool Recommande);

/// <summary>
/// Choix du canal (DOC-03) : canal demandé par l'émetteur s'il est joignable, sinon préférence du destinataire, sinon
/// cascade par type de destinataire. Si la loi impose un recommandé (SAN-11), un second envoi par recommandé électronique
/// (ou, à défaut, par courrier recommandé) s'ajoute à l'envoi simple.
/// </summary>
public static class ChoixCanal
{
    public static IReadOnlyList<CanalChoisi> Determiner(
        TypeDestinataire destinataire,
        Joignabilite joignabilite,
        Canal? souhaite,
        Canal? preference,
        bool recommandeRequis)
    {
        var choisis = new List<CanalChoisi>();

        // Le recommandé n'est jamais le canal de l'envoi simple : il s'y ajoute.
        foreach (var candidat in Candidats(destinataire, souhaite, preference))
        {
            if (candidat is not Canal.RecommandeElectronique && joignabilite.Peut(candidat))
            {
                choisis.Add(new CanalChoisi(candidat, Recommande: false));
                break;
            }
        }

        if (recommandeRequis)
        {
            if (joignabilite.Peut(Canal.RecommandeElectronique))
            {
                choisis.Add(new CanalChoisi(Canal.RecommandeElectronique, Recommande: true));
            }
            else if (joignabilite.Courrier)
            {
                choisis.Add(new CanalChoisi(Canal.Courrier, Recommande: true));
            }
        }

        return choisis;
    }

    /// <summary>Le recommandé exigé par la loi a-t-il pu être programmé ?</summary>
    public static bool RecommandeAssure(IReadOnlyList<CanalChoisi> choisis) => choisis.Any(c => c.Recommande);

    private static IEnumerable<Canal> Candidats(TypeDestinataire destinataire, Canal? souhaite, Canal? preference)
    {
        if (souhaite is { } s)
        {
            yield return s;
        }

        if (preference is { } p)
        {
            yield return p;
        }

        var cascade = destinataire switch
        {
            TypeDestinataire.Affilie => new[] { Canal.EBoxEntreprise, Canal.Email, Canal.Courrier, Canal.Portail },
            TypeDestinataire.Interne => [Canal.Email, Canal.Portail],
            _ => [Canal.Email, Canal.Sms, Canal.Courrier, Canal.Portail],
        };
        foreach (var canal in cascade)
        {
            yield return canal;
        }
    }
}

/// <summary>Reprises d'un envoi en échec : délais croissants, puis abandon (DOC-05).</summary>
public sealed record PolitiqueReprise(IReadOnlyList<TimeSpan> Delais)
{
    public static PolitiqueReprise Defaut { get; } = new([
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(12)]);

    /// <summary>Nombre maximal de tentatives : la première plus une par délai.</summary>
    public int TentativesMaximales => Delais.Count + 1;

    public TimeSpan DelaiApres(int tentativesEffectuees) =>
        tentativesEffectuees is >= 1 && tentativesEffectuees <= Delais.Count
            ? Delais[tentativesEffectuees - 1]
            : throw new DomainException("Aucune reprise prévue après cette tentative.");
}
