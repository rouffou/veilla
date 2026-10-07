using Sepp.BuildingBlocks.Domain;

namespace Sepp.Planification.Domain.Ressources;

/// <summary>PLA-01 : nature d'une ressource planifiable.</summary>
public enum TypeRessource
{
    /// <summary>Conseiller en prévention (CPMT ou autre discipline).</summary>
    Conseiller,
    Infirmier,
    Assistant,
    Salle,
    Cabine,

    /// <summary>Appareil de mesure : audiomètre, spiromètre, sonomètre…</summary>
    Appareil,
    UniteMobile,

    /// <summary>Chauffeur d'unité mobile (PLA-05).</summary>
    Chauffeur,
}

/// <summary>Agenda externe d'une ressource humaine (PLA-09).</summary>
public enum FournisseurAgenda
{
    Microsoft365,
    Google,
}

/// <summary>
/// Ressource planifiable (PLA-01, §15.3 : id, type, reference_id, competences). Les compétences sont des codes de
/// types d'acte que la ressource peut réaliser ou supporter (par ex. <c>AUDIOMETRIE</c> pour un audiomètre).
/// </summary>
public sealed class Ressource : AggregateRoot
{
    private Ressource()
    {
    }

    private Ressource(Guid id) : base(id)
    {
    }

    public TypeRessource Type { get; private set; }

    /// <summary>Intitulé de travail (« Dr A. — CPMT », « Salle 2 », « Audiomètre AU-12 ») : aucune donnée de santé.</summary>
    public string Libelle { get; private set; } = string.Empty;

    /// <summary>
    /// Référence externe : identifiant de l'utilisateur (claim <c>sub</c>) pour une ressource humaine — sert à limiter
    /// le CPMT à ses propres modèles d'agenda et au rapprochement des congés RH —, numéro d'inventaire pour un appareil.
    /// </summary>
    public string? ReferenceId { get; private set; }

    public IReadOnlyList<string> Competences { get; private set; } = [];

    /// <summary>Lieu de rattachement habituel.</summary>
    public Guid? LieuId { get; private set; }

    public bool Active { get; private set; } = true;

    public FournisseurAgenda? FournisseurAgenda { get; private set; }

    /// <summary>Identifiant du calendrier externe (adresse du compte professionnel).</summary>
    public string? CompteAgenda { get; private set; }

    public bool EstHumaine => Type is TypeRessource.Conseiller or TypeRessource.Infirmier or TypeRessource.Assistant or TypeRessource.Chauffeur;

    public static Ressource Creer(TypeRessource type, string libelle, string? referenceId, IEnumerable<string>? competences, Guid? lieuId)
    {
        if (string.IsNullOrWhiteSpace(libelle) || libelle.Trim().Length > 200)
        {
            throw new DomainException("L'intitulé de la ressource est obligatoire (200 caractères au plus).");
        }

        var reference = string.IsNullOrWhiteSpace(referenceId) ? null : referenceId.Trim();
        if (reference is { Length: > 100 })
        {
            throw new DomainException("La référence de la ressource est limitée à 100 caractères.");
        }

        return new Ressource(NewId())
        {
            Type = type,
            Libelle = libelle.Trim(),
            ReferenceId = reference,
            Competences = CodeMetier.NormaliserTous(competences, "Compétence"),
            LieuId = lieuId,
        };
    }

    public void DefinirCompetences(IEnumerable<string> competences) =>
        Competences = CodeMetier.NormaliserTous(competences, "Compétence");

    public bool PossedeCompetence(string typeActe) => Competences.Contains(typeActe, StringComparer.Ordinal);

    public void RattacherAgendaExterne(FournisseurAgenda fournisseur, string compte)
    {
        if (!EstHumaine)
        {
            throw new DomainException("Seule une ressource humaine peut synchroniser un agenda externe.");
        }

        if (string.IsNullOrWhiteSpace(compte) || compte.Trim().Length > 200)
        {
            throw new DomainException("Le compte de l'agenda externe est obligatoire (200 caractères au plus).");
        }

        FournisseurAgenda = fournisseur;
        CompteAgenda = compte.Trim();
    }

    public void Desactiver() => Active = false;
}

/// <summary>Origine d'une indisponibilité de ressource.</summary>
public enum SourceAbsence
{
    /// <summary>Congé importé de l'outil RH existant, en lecture seule (PLA-03).</summary>
    OutilRh,

    /// <summary>Occupation lue dans l'agenda Microsoft 365 / Google de la ressource (PLA-09).</summary>
    AgendaExterne,

    /// <summary>Absence déclarée par le planificateur (PLA-07).</summary>
    Saisie,
}

/// <summary>
/// Indisponibilité d'une ressource sur [Debut, Fin[ : congé RH, occupation d'agenda externe, absence déclarée.
/// Les indisponibilités importées sont identifiées par leur référence externe (réimport idempotent) et ne se modifient
/// jamais dans le SEPP : l'outil RH reste la source (lecture seule).
/// </summary>
public sealed class Absence : AggregateRoot
{
    private Absence()
    {
    }

    private Absence(Guid id) : base(id)
    {
    }

    public Guid RessourceId { get; private set; }

    public DateTimeOffset Debut { get; private set; }

    public DateTimeOffset Fin { get; private set; }

    public SourceAbsence Source { get; private set; }

    public string? ReferenceExterne { get; private set; }

    public static Absence Creer(Guid ressourceId, DateTimeOffset debut, DateTimeOffset fin, SourceAbsence source, string? referenceExterne)
    {
        Verifier(debut, fin);
        if (source != SourceAbsence.Saisie && string.IsNullOrWhiteSpace(referenceExterne))
        {
            throw new DomainException("Une indisponibilité importée porte la référence de sa source.");
        }

        return new Absence(NewId())
        {
            RessourceId = ressourceId,
            Debut = debut.ToUniversalTime(),
            Fin = fin.ToUniversalTime(),
            Source = source,
            ReferenceExterne = string.IsNullOrWhiteSpace(referenceExterne) ? null : referenceExterne.Trim(),
        };
    }

    /// <summary>Réimport : la source a modifié la période.</summary>
    /// <returns><c>true</c> si la période a changé.</returns>
    public bool Actualiser(DateTimeOffset debut, DateTimeOffset fin)
    {
        Verifier(debut, fin);
        if (debut == Debut && fin == Fin)
        {
            return false;
        }

        Debut = debut.ToUniversalTime();
        Fin = fin.ToUniversalTime();
        return true;
    }

    public bool Chevauche(DateTimeOffset debut, DateTimeOffset fin) => Debut < fin && debut < Fin;

    private static void Verifier(DateTimeOffset debut, DateTimeOffset fin)
    {
        if (fin <= debut)
        {
            throw new DomainException("La fin d'une indisponibilité doit suivre son début.");
        }
    }
}
