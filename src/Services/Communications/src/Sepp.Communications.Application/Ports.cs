using Sepp.BuildingBlocks.Domain;
using Sepp.Communications.Domain.Messages;

namespace Sepp.Communications.Application;

/// <summary>Adresse postale du destinataire, lue au moment de l'envoi et jamais conservée.</summary>
public sealed record AdressePostale(string Nom, string Rue, string Numero, string? Boite, string CodePostal, string Localite, string Pays);

/// <summary>
/// Coordonnées et préférences d'un destinataire, lues auprès du service propriétaire (Personnes, Affiliés) au moment de
/// l'envoi : Communications ne les conserve pas (minimisation, NF-14).
/// </summary>
/// <param name="CanalPrefere">Préférence de canal du destinataire, si le service propriétaire en connaît une.</param>
/// <param name="EBoxEntreprise">Identifiant eBox Entreprise (numéro BCE) de l'affilié.</param>
/// <param name="EBoxCitoyen">Identifiant eBox Citoyen (numéro d'identification) du travailleur.</param>
public sealed record Destinataire(
    TypeDestinataire Type,
    Guid Id,
    Language Langue,
    string? Nom,
    string? Email,
    string? Telephone,
    AdressePostale? Adresse,
    string? EBoxEntreprise,
    string? EBoxCitoyen,
    bool PortailActif,
    Canal? CanalPrefere)
{
    public Joignabilite Joignabilite => new(
        PortailActif,
        !string.IsNullOrWhiteSpace(Email),
        !string.IsNullOrWhiteSpace(Telephone),
        Adresse is not null,
        !string.IsNullOrWhiteSpace(EBoxEntreprise),
        !string.IsNullOrWhiteSpace(EBoxCitoyen));
}

/// <summary>
/// Annuaire des destinataires (DOC-03) : port vers les services Personnes (travailleur) et Affiliés (employeur), ou vers
/// l'annuaire des utilisateurs internes pour les alertes.
/// </summary>
public interface IAnnuaireDestinataires
{
    /// <returns>Le destinataire, ou <c>null</c> s'il est inconnu du service propriétaire.</returns>
    Task<Destinataire?> ObtenirAsync(TypeDestinataire type, Guid id, CancellationToken cancellationToken);

    /// <summary>Dirigeants à alerter lors d'un bris de glace : CPMT dirigeant (zone médicale), CPAP dirigeant (zone psychosociale).</summary>
    Task<IReadOnlyList<Destinataire>> ResoudreDirigeantsAsync(string zone, CancellationToken cancellationToken);
}

/// <summary>Liens vers les espaces sécurisés, insérés dans les notifications (jamais de donnée dans le lien : un identifiant opaque).</summary>
public sealed class OptionsLiens
{
    public string PortailTravailleur { get; set; } = "https://travailleur.invalid/messages";

    public string PortailEmployeur { get; set; } = "https://employeur.invalid/messages";

    public string Interne { get; set; } = "https://interne.invalid/messages";

    public string Pour(TypeDestinataire type, Guid messageId)
    {
        var racine = type switch
        {
            TypeDestinataire.Affilie => PortailEmployeur,
            TypeDestinataire.Interne => Interne,
            _ => PortailTravailleur,
        };
        return $"{racine.TrimEnd('/')}/{messageId:D}";
    }
}

/// <summary>Message à remettre à un canal, avec les coordonnées lues pour cet envoi.</summary>
public sealed record Envoi(Guid MessageId, Canal Canal, bool Recommande, Language Langue, string Sujet, string Corps, Destinataire Destinataire);

/// <summary>Preuve rendue par le canal : accusé de dépôt SMTP, accusé de recommandé, référence eBox, bordereau postal…</summary>
public sealed record ResultatEnvoi(string TypePreuve, string Reference, DateTimeOffset Date);

/// <summary>Échec d'un envoi ; <see cref="Definitive"/> si une nouvelle tentative est inutile (adresse refusée, canal non raccordé).</summary>
public sealed class ErreurEnvoiException : Exception
{
    public ErreurEnvoiException(string code, bool definitive, Exception? inner = null)
        : base(code, inner)
    {
        Code = code;
        Definitive = definitive;
    }

    public ErreurEnvoiException()
    {
        Code = "erreur-envoi";
    }

    public ErreurEnvoiException(string message) : base(message)
    {
        Code = message;
    }

    public ErreurEnvoiException(string message, Exception innerException) : base(message, innerException)
    {
        Code = message;
    }

    /// <summary>Code technique court, sans coordonnée du destinataire.</summary>
    public string Code { get; }

    public bool Definitive { get; }
}

/// <summary>Port d'un canal d'envoi (DOC-03) : un port par canal, avec un simulateur et, à terme, un adaptateur réel.</summary>
public interface ICanalEnvoi
{
    Canal Canal { get; }

    /// <exception cref="ErreurEnvoiException">Envoi impossible.</exception>
    Task<ResultatEnvoi> EnvoyerAsync(Envoi envoi, CancellationToken cancellationToken);
}

/// <summary>Notification dans l'espace sécurisé (portails).</summary>
public interface IPortailNotifications : ICanalEnvoi;

/// <summary>E-mail (SMTP).</summary>
public interface IEnvoiEmail : ICanalEnvoi;

/// <summary>SMS (passerelle d'un opérateur).</summary>
public interface IEnvoiSms : ICanalEnvoi;

/// <summary>Courrier postal, simple ou recommandé (prestataire d'impression et de dépôt).</summary>
public interface IEnvoiCourrier : ICanalEnvoi;

/// <summary>Recommandé électronique qualifié (prestataire eIDAS).</summary>
public interface IEnvoiRecommandeElectronique : ICanalEnvoi;

/// <summary>eBox Entreprise.</summary>
public interface IEBoxEntreprise : ICanalEnvoi;

/// <summary>eBox Citoyen.</summary>
public interface IEBoxCitoyen : ICanalEnvoi;

/// <summary>Filtre de recherche du journal des messages (DOC-05).</summary>
public sealed record FiltreMessages(
    TypeDestinataire? TypeDestinataire,
    Guid? DestinataireId,
    string? ObjetType,
    Guid? ObjetId,
    StatutMessage? Statut,
    Canal? Canal,
    int Page,
    int Taille);

public interface IMessageRepository
{
    Task<Message?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExisteParCleAsync(string cleIdempotence, CancellationToken cancellationToken);

    /// <summary>Messages dont la prochaine tentative est échue, du plus ancien au plus récent.</summary>
    Task<IReadOnlyList<Message>> ListerEchusAsync(DateTimeOffset maintenant, int limite, CancellationToken cancellationToken);

    Task<IReadOnlyList<Message>> ListerParObjetAsync(string objetType, Guid objetId, CancellationToken cancellationToken);

    Task<(IReadOnlyList<Message> Messages, int Total)> RechercherAsync(FiltreMessages filtre, CancellationToken cancellationToken);

    /// <summary>
    /// Réserve un message échu pour un envoi (compare-and-swap sur la prochaine tentative) : deux instances du service ne
    /// l'envoient pas en même temps. <c>false</c> si une autre instance l'a déjà réservé.
    /// </summary>
    Task<bool> ReserverAsync(Message message, DateTimeOffset jusqua, CancellationToken cancellationToken);

    void Add(Message message);
}
