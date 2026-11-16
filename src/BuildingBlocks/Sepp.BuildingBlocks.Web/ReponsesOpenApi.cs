using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Sepp.BuildingBlocks.Web;

/// <summary>Corps d'une réponse de création qui ne rend que l'identifiant (<c>{ "id": … }</c>).</summary>
public sealed record IdentifiantCree(Guid Id);

/// <summary>Codes d'erreur HTTP que <see cref="ResultExtensions.ToProblem"/> traduit depuis les erreurs fonctionnelles (<c>ErrorKind</c>).</summary>
public enum ErreurHttp
{
    /// <summary>400 : <c>ErrorKind.Validation</c>.</summary>
    Requete = 400,

    /// <summary>403 : <c>ErrorKind.Forbidden</c> (permission ou périmètre).</summary>
    Interdit = 403,

    /// <summary>404 : <c>ErrorKind.NotFound</c>.</summary>
    Introuvable = 404,

    /// <summary>409 : <c>ErrorKind.Conflict</c>.</summary>
    Conflit = 409,

    /// <summary>422 : <c>ErrorKind.Unprocessable</c>.</summary>
    NonTraitable = 422,
}

/// <summary>
/// Description OpenAPI des réponses réelles d'un endpoint (ARC-30, #476) : type du corps de succès et codes d'erreur
/// <c>problem+json</c> que <see cref="ResultExtensions.ToProblem"/> peut produire pour ce cas d'usage. Métadonnées seules :
/// aucun effet sur le comportement. Les instantanés <c>docs/api/openapi</c> les publient, et les tests de contrat
/// (<c>tests/Sepp.Testing.Contrats</c>) y confrontent les champs lus par les consommateurs et les doubles de test.
/// </summary>
public static class ReponsesOpenApi
{
    /// <summary>Succès 200 avec un corps de type <typeparamref name="T"/>, et les erreurs <paramref name="erreurs"/>.</summary>
    public static RouteHandlerBuilder Retourne<T>(this RouteHandlerBuilder builder, params ErreurHttp[] erreurs) =>
        builder.Produces<T>(200).Erreurs(erreurs);

    /// <summary>Succès 201 (création) avec un corps de type <typeparamref name="T"/>, et les erreurs <paramref name="erreurs"/>.</summary>
    public static RouteHandlerBuilder RetourneCree<T>(this RouteHandlerBuilder builder, params ErreurHttp[] erreurs) =>
        builder.Produces<T>(201).Erreurs(erreurs);

    /// <summary>Succès 204 (cas d'usage sans valeur de retour), et les erreurs <paramref name="erreurs"/>.</summary>
    public static RouteHandlerBuilder RetourneSansContenu(this RouteHandlerBuilder builder, params ErreurHttp[] erreurs) =>
        builder.Produces(204).Erreurs(erreurs);

    /// <summary>Codes d'erreur <c>application/problem+json</c> (RFC 9457) de l'endpoint.</summary>
    public static RouteHandlerBuilder Erreurs(this RouteHandlerBuilder builder, params ErreurHttp[] erreurs)
    {
        ArgumentNullException.ThrowIfNull(builder);
        foreach (var erreur in erreurs)
        {
            builder.ProducesProblem((int)erreur);
        }

        return builder;
    }
}
