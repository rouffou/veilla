using System.Security.Cryptography;
using System.Text;

using Sepp.BuildingBlocks.Domain;

namespace Sepp.Communications.Domain.Messages;

/// <summary>Contenu d'un message : sujet, corps et nature (générique ou détaillé).</summary>
/// <param name="EstGenerique">Notification sans information personnelle : seule forme admise pour l'e-mail et le SMS (DOC-03).</param>
public sealed record ContenuMessage(string Sujet, string Corps, bool EstGenerique)
{
    public const int LongueurMaximaleSujet = 200;
    public const int LongueurMaximaleCorps = 4_000;
}

/// <summary>Données de création d'un message.</summary>
/// <param name="Id">Identifiant du message, connu avant sa création pour composer le lien opaque de la notification.</param>
/// <param name="CleIdempotence">Un même fait métier ne produit qu'un message (DOC-03 : envoi idempotent).</param>
/// <param name="ObjetType">Type de l'objet à l'origine du message (<c>document</c>, <c>rendez-vous</c>, <c>audit-entree</c>…).</param>
public sealed record NouveauMessage(
    Guid Id,
    TypeMessage Type,
    Canal Canal,
    bool Recommande,
    TypeDestinataire TypeDestinataire,
    Guid DestinataireId,
    Language Langue,
    string ObjetType,
    Guid ObjetId,
    string CleIdempotence,
    ContenuMessage Contenu);

/// <summary>
/// Message envoyé à un destinataire par un canal (§15.3 <c>message</c> : id, canal, destinataire_id, objet, statut ; DOC-03 à
/// DOC-05). Le message ne conserve aucune coordonnée (adresse, e-mail, téléphone) : elles sont lues auprès des services
/// propriétaires au moment de l'envoi. L'e-mail et le SMS ne portent qu'une notification générique et un lien.
/// </summary>
public sealed class Message : AggregateRoot
{
    public const int LongueurMaximaleSms = 320;

    private readonly List<PreuveEnvoi> _preuves = [];

    private Message()
    {
    }

    private Message(Guid id) : base(id)
    {
    }

    public TypeMessage Type { get; private set; }

    public Canal Canal { get; private set; }

    /// <summary>Envoi recommandé exigé par la loi (SAN-11) : recommandé électronique ou courrier recommandé.</summary>
    public bool Recommande { get; private set; }

    public TypeDestinataire TypeDestinataire { get; private set; }

    public Guid DestinataireId { get; private set; }

    public Language Langue { get; private set; }

    public string ObjetType { get; private set; } = string.Empty;

    public Guid ObjetId { get; private set; }

    public string CleIdempotence { get; private set; } = string.Empty;

    public string Sujet { get; private set; } = string.Empty;

    public string Corps { get; private set; } = string.Empty;

    /// <summary>Le contenu est une notification générique (sans information personnelle).</summary>
    public bool ContenuGenerique { get; private set; }

    public StatutMessage Statut { get; private set; }

    public int Tentatives { get; private set; }

    /// <summary>Prochaine tentative d'envoi ; pendant un envoi, une réservation temporaire évite les envois en double.</summary>
    public DateTimeOffset? ProchaineTentative { get; private set; }

    /// <summary>Code technique de la dernière erreur (jamais de coordonnée du destinataire).</summary>
    public string? DerniereErreur { get; private set; }

    public DateTimeOffset CreeLe { get; private set; }

    public DateTimeOffset? EnvoyeLe { get; private set; }

    public IReadOnlyList<PreuveEnvoi> Preuves => _preuves.AsReadOnly();

    public static Guid NouvelIdentifiant() => NewId();

    public static Message Creer(NouveauMessage n, DateTimeOffset maintenant)
    {
        if (n.DestinataireId == Guid.Empty || n.ObjetId == Guid.Empty)
        {
            throw new DomainException("Le destinataire et l'objet du message sont obligatoires.");
        }

        if (string.IsNullOrWhiteSpace(n.CleIdempotence) || n.CleIdempotence.Length > 200)
        {
            throw new DomainException("La clé d'idempotence est obligatoire (200 caractères au plus).");
        }

        if (string.IsNullOrWhiteSpace(n.ObjetType) || n.ObjetType.Length > 50)
        {
            throw new DomainException("Le type d'objet est obligatoire (50 caractères au plus).");
        }

        if (string.IsNullOrWhiteSpace(n.Contenu.Sujet) || n.Contenu.Sujet.Length > ContenuMessage.LongueurMaximaleSujet)
        {
            throw new DomainException($"Le sujet est obligatoire ({ContenuMessage.LongueurMaximaleSujet} caractères au plus).");
        }

        if (string.IsNullOrWhiteSpace(n.Contenu.Corps) || n.Contenu.Corps.Length > ContenuMessage.LongueurMaximaleCorps)
        {
            throw new DomainException($"Le corps du message est obligatoire ({ContenuMessage.LongueurMaximaleCorps} caractères au plus).");
        }

        // DOC-03 : un e-mail ou un SMS ne contient jamais de donnée sensible, seulement une notification et un lien.
        if (!n.Canal.PeutPorterUnContenuDetaille() && !n.Contenu.EstGenerique)
        {
            throw new DomainException($"Le canal {n.Canal} n'accepte qu'une notification générique (aucune donnée sensible par e-mail ou SMS).");
        }

        if (n.Canal == Canal.Sms && n.Contenu.Corps.Length > LongueurMaximaleSms)
        {
            throw new DomainException($"Un SMS est limité à {LongueurMaximaleSms} caractères.");
        }

        if (n.Recommande && n.Canal is not (Canal.RecommandeElectronique or Canal.Courrier))
        {
            throw new DomainException("Un envoi recommandé passe par le recommandé électronique ou par le courrier.");
        }

        if (n.Canal == Canal.RecommandeElectronique && !n.Recommande)
        {
            throw new DomainException("Le recommandé électronique est un envoi recommandé.");
        }

        return new Message(n.Id)
        {
            Type = n.Type,
            Canal = n.Canal,
            Recommande = n.Recommande,
            TypeDestinataire = n.TypeDestinataire,
            DestinataireId = n.DestinataireId,
            Langue = n.Langue,
            ObjetType = n.ObjetType.Trim(),
            ObjetId = n.ObjetId,
            CleIdempotence = n.CleIdempotence.Trim(),
            Sujet = n.Contenu.Sujet,
            Corps = n.Contenu.Corps,
            ContenuGenerique = n.Contenu.EstGenerique,
            Statut = StatutMessage.EnAttente,
            ProchaineTentative = maintenant,
            CreeLe = maintenant,
        };
    }

    /// <summary>Empreinte SHA-256 du sujet et du corps, conservée dans la preuve d'envoi.</summary>
    public string EmpreinteContenu() =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{Sujet}\n{Corps}")));

    public bool EstEchu(DateTimeOffset maintenant) =>
        Statut is StatutMessage.EnAttente or StatutMessage.EnEchec && ProchaineTentative is { } prochaine && prochaine <= maintenant;

    /// <summary>L'envoi a réussi : la preuve (accusé de dépôt, référence externe) est conservée (DOC-05).</summary>
    public PreuveEnvoi EnregistrerEnvoi(DateTimeOffset date, string typePreuve, string reference)
    {
        if (Statut is not (StatutMessage.EnAttente or StatutMessage.EnEchec))
        {
            throw new DomainException($"Un message {Statut} ne peut pas être marqué envoyé.");
        }

        Tentatives++;
        Statut = StatutMessage.Envoye;
        EnvoyeLe = date;
        ProchaineTentative = null;
        DerniereErreur = null;
        var preuve = new PreuveEnvoi(Guid.CreateVersion7(), typePreuve, date, reference, EmpreinteContenu());
        _preuves.Add(preuve);
        return preuve;
    }

    /// <summary>
    /// L'envoi a échoué : nouvelle tentative selon la politique de reprise, ou abandon si l'erreur est définitive ou si les
    /// reprises sont épuisées. <paramref name="code"/> est un code technique, jamais une coordonnée du destinataire.
    /// </summary>
    public void EnregistrerEchec(DateTimeOffset date, string code, bool definitif, PolitiqueReprise politique)
    {
        if (Statut is not (StatutMessage.EnAttente or StatutMessage.EnEchec))
        {
            throw new DomainException($"Un message {Statut} ne peut pas être mis en échec.");
        }

        Tentatives++;
        DerniereErreur = code.Length > 100 ? code[..100] : code;
        if (definitif || Tentatives >= politique.TentativesMaximales)
        {
            Statut = StatutMessage.Abandonne;
            ProchaineTentative = null;
            return;
        }

        Statut = StatutMessage.EnEchec;
        ProchaineTentative = date + politique.DelaiApres(Tentatives);
    }

    /// <summary>Annule un message qui n'est pas encore parti (par ex. rendez-vous annulé).</summary>
    public void Annuler(string motif)
    {
        if (Statut is not (StatutMessage.EnAttente or StatutMessage.EnEchec))
        {
            throw new DomainException($"Un message {Statut} ne peut plus être annulé.");
        }

        Statut = StatutMessage.Annule;
        ProchaineTentative = null;
        DerniereErreur = motif.Length > 100 ? motif[..100] : motif;
    }

    /// <summary>Relance manuelle d'un message abandonné (DOC-05) : il repart avec un nouveau cycle de reprises.</summary>
    public void Relancer(DateTimeOffset maintenant)
    {
        if (Statut != StatutMessage.Abandonne)
        {
            throw new DomainException("Seul un message abandonné peut être relancé.");
        }

        Statut = StatutMessage.EnAttente;
        Tentatives = 0;
        ProchaineTentative = maintenant;
        DerniereErreur = null;
    }
}

/// <summary>
/// Preuve d'envoi (§15.3 <c>preuve_envoi</c> : id, message_id, type, horodatage, reference) : accusé de dépôt SMTP, accusé du
/// recommandé électronique, référence eBox, bordereau de dépôt postal… L'empreinte porte sur le contenu envoyé.
/// </summary>
public sealed class PreuveEnvoi : Entity
{
    private PreuveEnvoi()
    {
    }

    internal PreuveEnvoi(Guid id, string type, DateTimeOffset horodatage, string reference, string empreinte) : base(id)
    {
        Type = type.Length > 50 ? type[..50] : type;
        Horodatage = horodatage;
        Reference = reference.Length > 500 ? reference[..500] : reference;
        Empreinte = empreinte;
    }

    public string Type { get; private set; } = string.Empty;

    public DateTimeOffset Horodatage { get; private set; }

    public string Reference { get; private set; } = string.Empty;

    public string Empreinte { get; private set; } = string.Empty;
}
