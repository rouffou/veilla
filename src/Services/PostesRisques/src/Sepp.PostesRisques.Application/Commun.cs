using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;

namespace Sepp.PostesRisques.Application;

/// <summary>Contrôles d'accès communs : permission de la matrice §3.3 puis périmètre de l'affilié (ADR 0005).</summary>
internal static class Acces
{
    public static Error? Verifier(ICurrentUser user, IPerimetreAffilies perimetre, Guid affilieId, params string[] uneDesPermissions)
    {
        if (!uneDesPermissions.Any(user.HasPermission))
        {
            return Error.Forbidden("postes-risques.interdit", "Vous n'avez pas le droit d'effectuer cette opération.");
        }

        return perimetre.PeutAcceder(affilieId)
            ? null
            : Error.Forbidden("perimetre.interdit", "Cet affilié ne fait pas partie de votre périmètre.");
    }

    public static Error? Permission(ICurrentUser user, string permission, string message) =>
        user.HasPermission(permission) ? null : Error.Forbidden("postes-risques.interdit", message);

    /// <summary>Un utilisateur externe doit toujours préciser l'affilié qu'il consulte.</summary>
    public static Error? Filtre(IPerimetreAffilies perimetre, Guid? affilieId) =>
        affilieId is { } id
            ? perimetre.PeutAcceder(id) ? null : Error.Forbidden("perimetre.interdit", "Cet affilié ne fait pas partie de votre périmètre.")
            : perimetre.EstExterne ? Error.Validation("perimetre.affilie-obligatoire", "Précisez l'affilié consulté.") : null;
}

/// <summary>
/// Durée de conservation de l'historique des listes nominatives (AFF-31). La valeur légale est le paramètre
/// <c>SANTE.LISTES_NOMINATIVES.CONSERVATION</c> du service Référentiels, reçu par l'événement ParametreLegalModifie ;
/// tant qu'aucune valeur n'a été reçue, la valeur de configuration <c>ListesNominatives:ConservationAnnees</c>
/// (5 ans par défaut, valeur initiale du paramètre légal) s'applique.
/// </summary>
public sealed class PolitiqueConservationListes(IProjectionRepository projections, ConservationListesOptions options)
{
    public const string CodeParametre = "SANTE.LISTES_NOMINATIVES.CONSERVATION";

    public async Task<int> AnneesAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var parametre = await projections.ParametreApplicableAsync(CodeParametre, date, cancellationToken);
        var annees = parametre?.Unite switch
        {
            "Annees" => (int)Math.Ceiling(parametre.Valeur),
            "Mois" => (int)Math.Ceiling(parametre.Valeur / 12),
            _ => options.AnneesParDefaut,
        };
        return Math.Max(annees, 1);
    }
}

public sealed class ConservationListesOptions
{
    /// <summary>Valeur par défaut, alignée sur la valeur initiale du paramètre légal (5 ans).</summary>
    public int AnneesParDefaut { get; set; } = 5;
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
        return !int.TryParse(normalisee, out _) && Enum.TryParse(normalisee, ignoreCase: true, out resultat);
    }
}
