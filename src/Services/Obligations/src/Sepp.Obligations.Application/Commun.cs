using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Obligations.Domain.Obligations;

namespace Sepp.Obligations.Application;

/// <summary>Contrôles d'accès communs : permission de la matrice §3.3 puis périmètre de l'affilié (ADR 0005).</summary>
internal static class Acces
{
    public static Error? Permission(ICurrentUser user, string permission) =>
        user.HasPermission(permission) ? null : Error.Forbidden("obligations.interdit", "Vous n'avez pas le droit d'effectuer cette opération.");

    public static Error? Affilie(ICurrentUser user, IPerimetreAffilies perimetre, Guid affilieId, string permission) =>
        Permission(user, permission)
        ?? (perimetre.PeutAcceder(affilieId) ? null : Error.Forbidden("perimetre.interdit", "Cet affilié ne fait pas partie de votre périmètre."));

    /// <summary>
    /// Une obligation est visible si l'affilié est dans le périmètre ; un profil externe ne voit pas les types
    /// confidentiels (protection de la maternité, démarches du travailleur).
    /// </summary>
    public static bool PeutVoir(IPerimetreAffilies perimetre, Obligation obligation) =>
        perimetre.PeutAcceder(obligation.AffilieId) && !(perimetre.EstExterne && obligation.Type.EstConfidentiel());

    public static Error ObligationInconnue(Guid id) => Error.NotFound("obligation.inconnue", $"Obligation {id} inconnue.");
}

/// <summary>Date du jour en Belgique : les échéances sont des dates civiles belges (DAT-08).</summary>
public static class HorlogeBelge
{
    private static readonly TimeZoneInfo Bruxelles = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

    public static DateOnly AujourdHui(this TimeProvider clock) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Bruxelles).DateTime);
}

/// <summary>Réglages de lecture (configuration <c>Obligations</c>) : fenêtres et horizons, pas des délais légaux.</summary>
public sealed class OptionsObligations
{
    /// <summary>SAN-03 : fenêtre de regroupement par défaut, en jours.</summary>
    public int FenetreRegroupementJours { get; set; } = 30;

    /// <summary>AFF-32 : une obligation à planifier dont l'échéance tombe dans cet horizon déclenche l'alerte.</summary>
    public int HorizonAlertesJours { get; set; } = 30;

    /// <summary>Tableau de bord (POR-02) : horizon des obligations « dues » non planifiées.</summary>
    public int HorizonDuesJours { get; set; } = 90;
}

internal static class Enumerations
{
    /// <summary>Lit une valeur d'énumération sans tenir compte de la casse, des tirets ni des soulignés.</summary>
    public static bool TryParse<TEnum>(string? valeur, out TEnum resultat)
        where TEnum : struct, Enum
    {
        resultat = default;
        if (string.IsNullOrWhiteSpace(valeur))
        {
            return false;
        }

        var normalisee = valeur.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).Trim();
        return !int.TryParse(normalisee, out _) && Enum.TryParse(normalisee, ignoreCase: true, out resultat) && Enum.IsDefined(resultat);
    }
}
