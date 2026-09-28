namespace Sepp.BuildingBlocks.Application.Auditing;

/// <summary>Nature de l'accès journalisé (NF-04).</summary>
public enum ActionAudit
{
    Lecture,
    Creation,
    Modification,
    Suppression,
    Export,
}

/// <summary>Zones de sensibilité (ARC-04) : partition du journal d'audit et périmètre de consultation (PSY-20).</summary>
public static class ZonesSensibilite
{
    public const string Standard = "standard";
    public const string Medicale = "medicale";
    public const string Psychosociale = "psychosociale";

    public static IReadOnlyList<string> Toutes { get; } = [Standard, Medicale, Psychosociale];
}

/// <summary>
/// Journal d'audit des accès aux données sensibles (NF-04, §3.3). Chaque service qui lit ou modifie
/// un dossier de santé ou psychosocial, ou une donnée protégée, y enregistre une trace ; le service Audit
/// la chaîne dans son journal infalsifiable.
/// </summary>
/// <remarks>
/// La trace ne contient que des identifiants, des types, l'action et le motif saisi : jamais de donnée clinique
/// ou psychosociale (ARC-06). Le motif est un texte libre court (<see cref="MotifAcces.LongueurMaximale"/> caractères)
/// qui ne doit pas contenir de donnée de santé.
/// </remarks>
public interface IAuditTrail
{
    /// <summary>
    /// Ajoute la trace à l'outbox du service : elle est écrite par le prochain <see cref="IUnitOfWork.SaveChangesAsync"/>,
    /// donc dans la même transaction que la modification (ARC-32). À utiliser pour les créations, modifications et suppressions.
    /// </summary>
    /// <exception cref="ArgumentException">Motif absent pour un bris de glace, ou motif trop long (voir <see cref="MotifAcces.Verifier"/>).</exception>
    void Enregistrer(ActionAudit action, string objetType, Guid objetId, string? motif = null, bool brisDeGlace = false);

    /// <summary>
    /// Enregistre immédiatement la trace d'une lecture (écriture de l'outbox validée avant de rendre la donnée).
    /// Si l'enregistrement échoue, la lecture doit échouer.
    /// </summary>
    Task EnregistrerLectureAsync(string objetType, Guid objetId, string? motif, bool brisDeGlace, CancellationToken cancellationToken);
}

/// <summary>Règles du motif d'accès (§3.3) : obligatoire pour un bris de glace, court, sans donnée de santé.</summary>
public static class MotifAcces
{
    public const int LongueurMaximale = 300;

    /// <summary>Vérifie le motif avant l'accès ; à appeler par le cas d'usage pour renvoyer une erreur de validation.</summary>
    public static Error? Verifier(string? motif, bool brisDeGlace)
    {
        var normalise = Normaliser(motif);
        if (brisDeGlace && normalise is null)
        {
            return Error.Validation("audit.motif-obligatoire", "Un accès « bris de glace » exige un motif.");
        }

        return normalise is { Length: > LongueurMaximale }
            ? Error.Validation("audit.motif-trop-long", $"Le motif d'accès est limité à {LongueurMaximale} caractères.")
            : null;
    }

    public static string? Normaliser(string? motif) => string.IsNullOrWhiteSpace(motif) ? null : motif.Trim();
}
