using System.Security.Claims;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Web;

namespace Sepp.Bff.Travailleur.Securite;

/// <summary>
/// Périmètre de l'utilisateur du portail travailleur (§3.3, POR-10) : rôle <c>travailleur</c> et claim <c>personne_id</c>
/// (valeur unique, identifiant de la personne dans le service Personnes). Le travailleur ne voit que ses propres données :
/// l'identifiant de la personne n'est <b>jamais</b> lu dans la route, la requête ou le corps, toujours dans le jeton.
/// Les services aval le revérifient à partir du même jeton (défense en profondeur).
/// </summary>
public static class PerimetreTravailleur
{
    public const string ClaimPersonne = "personne_id";
    public const string Politique = "portail-travailleur";

    /// <summary>Clé de <see cref="HttpContext.Items"/> où le filtre dépose l'identifiant validé.</summary>
    private const string CleItem = "sepp.bff.travailleur.personne";

    /// <summary>Identifiant de la personne du jeton ; <c>null</c> si le claim est absent, répété ou n'est pas un UUID.</summary>
    public static Guid? Personne(ClaimsPrincipal utilisateur)
    {
        var claims = utilisateur.FindAll(ClaimPersonne).Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList();
        return claims is [var seule] && Guid.TryParse(seule, out var id) && id != Guid.Empty ? id : null;
    }

    /// <summary>Personne du jeton pour un cas d'usage ; la politique des routes garantit sa présence.</summary>
    public static Guid PersonneObligatoire(HttpContext http) =>
        http.Items[CleItem] is Guid id ? id : throw new InvalidOperationException("Le filtre de périmètre n'a pas été exécuté.");

    /// <summary>Filtre du groupe de routes : 403 sans appel aval si le jeton ne désigne pas exactement une personne.</summary>
    public static async ValueTask<object?> FiltrerAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (Personne(http.User) is not { } personne)
        {
            return ResultExtensions.ToProblem(Error.Forbidden("perimetre.interdit", "Votre compte n'est associé à aucune personne : contactez le SEPP."));
        }

        http.Items[CleItem] = personne;
        return await next(context);
    }
}
