using System.Security.Claims;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Web;

namespace Sepp.Bff.Employeur.Securite;

/// <summary>
/// Périmètre de l'utilisateur du portail (§3.3, POR-01) : rôle <c>employeur</c> ou <c>sipp</c> et affiliés représentés,
/// lus dans le claim multivalué <c>affilie_id</c> (claim répété, tableau JSON ou valeurs séparées par des virgules ;
/// les valeurs qui ne sont pas des UUID sont ignorées). Le BFF borne toutes ses requêtes à ce périmètre ; les
/// services aval le revérifient à partir du même jeton (défense en profondeur).
/// </summary>
public static class PerimetreEmployeur
{
    public const string ClaimAffilie = "affilie_id";
    public const string Politique = "portail-employeur";

    public static readonly string[] RolesAutorises = [Roles.Employeur, Roles.Sipp];

    public static IReadOnlyList<Guid> Affilies(ClaimsPrincipal utilisateur) =>
        utilisateur.FindAll(ClaimAffilie)
            .SelectMany(c => c.Value.Split([',', '[', ']', '"', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(v => Guid.TryParse(v, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

    public static bool PeutAcceder(ClaimsPrincipal utilisateur, Guid affilieId) => Affilies(utilisateur).Contains(affilieId);

    /// <summary>Filtre des routes <c>/affilies/{affilieId}</c> : 403 si l'affilié n'est pas dans le jeton, sans appel aval.</summary>
    public static async ValueTask<object?> FiltrerAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (http.Request.RouteValues.TryGetValue("affilieId", out var valeur)
            && Guid.TryParse(valeur?.ToString(), out var affilieId)
            && !PeutAcceder(http.User, affilieId))
        {
            return ResultExtensions.ToProblem(Error.Forbidden("perimetre.interdit", "Cet affilié ne fait pas partie de votre périmètre."));
        }

        return await next(context);
    }
}
