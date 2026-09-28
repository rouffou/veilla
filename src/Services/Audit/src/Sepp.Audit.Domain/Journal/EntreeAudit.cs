using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Sepp.BuildingBlocks.Domain;

namespace Sepp.Audit.Domain.Journal;

/// <summary>Dernier maillon connu d'une chaîne : numéro et empreinte (NF-04).</summary>
public sealed record MaillonChaine(long Numero, string Empreinte)
{
    /// <summary>Empreinte précédant la toute première entrée d'une zone.</summary>
    public static readonly MaillonChaine Origine = new(0, new string('0', 64));
}

/// <summary>Accès à journaliser, tel que reçu du service émetteur.</summary>
public sealed record TraceAcces(
    Guid EvenementId,
    DateTimeOffset Horodatage,
    Zone Zone,
    string Service,
    string UtilisateurId,
    string Role,
    ActionAudit Action,
    string ObjetType,
    Guid ObjetId,
    string? Motif,
    bool BrisDeGlace);

/// <summary>
/// Entrée du journal d'audit (§15.3 <c>entree_audit</c>, NF-04) : ajout seul, jamais modifiée.
/// Les entrées d'une même zone forment une chaîne : chacune porte l'empreinte SHA-256 de la précédente
/// et sa propre empreinte, calculée sur tous ses champs métier. Toute modification ou suppression d'une
/// ligne rompt la chaîne et est détectée par <see cref="VerificationChaine"/>.
/// </summary>
/// <remarks>
/// Aucune donnée clinique ou psychosociale : identifiants, types, action et motif saisi uniquement (ARC-06).
/// </remarks>
public sealed class EntreeAudit : AggregateRoot
{
    public const int LongueurMaximaleMotif = 300;
    public const int LongueurMaximaleUtilisateur = 100;
    public const int LongueurMaximaleRole = 500;
    public const int LongueurMaximaleService = 50;
    public const int LongueurMaximaleObjetType = 100;

    private EntreeAudit()
    {
    }

    private EntreeAudit(MaillonChaine precedent, TraceAcces trace) : base(NewId())
    {
        if (trace.EvenementId == Guid.Empty)
        {
            throw new DomainException("L'identifiant de l'événement source est obligatoire.");
        }

        if (trace.ObjetId == Guid.Empty)
        {
            throw new DomainException("L'identifiant de l'objet accédé est obligatoire.");
        }

        var motif = string.IsNullOrWhiteSpace(trace.Motif) ? null : trace.Motif.Trim();
        if (trace.BrisDeGlace && motif is null)
        {
            // §3.3 : un accès « bris de glace » est toujours motivé.
            throw new DomainException("Un accès « bris de glace » exige un motif.");
        }

        if (motif is { Length: > LongueurMaximaleMotif })
        {
            throw new DomainException($"Le motif d'accès est limité à {LongueurMaximaleMotif} caractères.");
        }

        Zone = trace.Zone;
        Numero = precedent.Numero + 1;
        EvenementSourceId = trace.EvenementId;
        Horodatage = Tronquer(trace.Horodatage);
        UtilisateurId = Requis(trace.UtilisateurId, "utilisateur", LongueurMaximaleUtilisateur);
        Role = (trace.Role ?? string.Empty).Trim() is { Length: <= LongueurMaximaleRole } role
            ? role
            : throw new DomainException($"Le rôle est limité à {LongueurMaximaleRole} caractères.");
        Service = Requis(trace.Service, "service", LongueurMaximaleService);
        Action = trace.Action;
        ObjetType = Requis(trace.ObjetType, "type d'objet", LongueurMaximaleObjetType);
        ObjetId = trace.ObjetId;
        Motif = motif;
        BrisDeGlace = trace.BrisDeGlace;
        EmpreintePrecedente = precedent.Empreinte;
        Empreinte = CalculerEmpreinte();
    }

    public Zone Zone { get; private set; }

    /// <summary>Rang dans la chaîne de la zone, sans trou (1, 2, 3…).</summary>
    public long Numero { get; private set; }

    /// <summary>Identifiant de l'événement reçu : clé d'idempotence (ARC-31).</summary>
    public Guid EvenementSourceId { get; private set; }

    /// <summary>Instant de l'accès, à la microseconde (précision de PostgreSQL).</summary>
    public DateTimeOffset Horodatage { get; private set; }

    public string UtilisateurId { get; private set; } = string.Empty;

    /// <summary>Rôles de l'utilisateur au moment de l'accès, séparés par des virgules.</summary>
    public string Role { get; private set; } = string.Empty;

    /// <summary>Service qui a servi l'accès.</summary>
    public string Service { get; private set; } = string.Empty;

    public ActionAudit Action { get; private set; }

    public string ObjetType { get; private set; } = string.Empty;

    public Guid ObjetId { get; private set; }

    /// <summary>Motif saisi (texte court, sans donnée de santé) ; obligatoire pour un bris de glace.</summary>
    public string? Motif { get; private set; }

    public bool BrisDeGlace { get; private set; }

    public string EmpreintePrecedente { get; private set; } = string.Empty;

    public string Empreinte { get; private set; } = string.Empty;

    public MaillonChaine Maillon => new(Numero, Empreinte);

    /// <summary>Ajoute un accès à la chaîne de sa zone, à la suite du dernier maillon.</summary>
    public static EntreeAudit Enregistrer(MaillonChaine precedent, TraceAcces trace) => new(precedent, trace);

    /// <summary>
    /// Empreinte SHA-256 (hexadécimal minuscule) de la représentation canonique de l'entrée : chaque champ est
    /// préfixé par sa longueur, ce qui rend la représentation non ambiguë.
    /// </summary>
    public string CalculerEmpreinte()
    {
        var canonique = new StringBuilder();
        void Champ(string valeur) => canonique.Append(valeur.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(valeur).Append(';');

        Champ(Id.ToString("D"));
        Champ(EvenementSourceId.ToString("D"));
        Champ(Zone.Code());
        Champ(Numero.ToString(CultureInfo.InvariantCulture));
        Champ(Horodatage.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture));
        Champ(UtilisateurId);
        Champ(Role);
        Champ(Service);
        Champ(Action.Code());
        Champ(ObjetType);
        Champ(ObjetId.ToString("D"));
        Champ(Motif ?? string.Empty);
        Champ(BrisDeGlace ? "1" : "0");
        Champ(EmpreintePrecedente);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonique.ToString())));
    }

    /// <summary>L'empreinte stockée correspond-elle au contenu de la ligne ?</summary>
    public bool EstIntegre() => string.Equals(Empreinte, CalculerEmpreinte(), StringComparison.Ordinal);

    private static DateTimeOffset Tronquer(DateTimeOffset instant)
    {
        var ticks = instant.UtcTicks;
        return new DateTimeOffset(ticks - (ticks % 10), TimeSpan.Zero);
    }

    private static string Requis(string? valeur, string nom, int longueurMaximale) =>
        string.IsNullOrWhiteSpace(valeur)
            ? throw new DomainException($"Le champ « {nom} » est obligatoire.")
            : valeur.Trim() is { } v && v.Length <= longueurMaximale
                ? v
                : throw new DomainException($"Le champ « {nom} » est limité à {longueurMaximale} caractères.");
}
