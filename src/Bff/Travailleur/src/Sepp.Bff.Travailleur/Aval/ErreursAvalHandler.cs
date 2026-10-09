using System.Net;

using Microsoft.AspNetCore.Diagnostics;

namespace Sepp.Bff.Travailleur.Aval;

/// <summary>
/// Traduit les échecs des services aval en ProblemDetails (RFC 9457) cohérents pour le portail :
/// erreurs fonctionnelles (400, 403, 404, 409, 422) relayées avec leur code ; service injoignable → 503 explicite ;
/// réponse inattendue (5xx, 401 du service, contrat invalide) → 502, sans détail technique.
/// </summary>
public sealed class ErreursAvalHandler(ILogger<ErreursAvalHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        IResult? resultat = exception switch
        {
            ServiceIndisponibleException e => Indisponible(e),
            ErreurAvalException e => Traduire(e),
            _ => null,
        };

        if (resultat is null)
        {
            return false;
        }

        await resultat.ExecuteAsync(httpContext);
        return true;
    }

    private IResult Indisponible(ServiceIndisponibleException e)
    {
        logger.LogWarning(e, "Service aval {Service} indisponible", e.Service);
        return Probleme(StatusCodes.Status503ServiceUnavailable, "service-aval.indisponible",
            "Ce service est momentanément indisponible. Veuillez réessayer dans quelques instants.", e.Service);
    }

    private IResult Traduire(ErreurAvalException e)
    {
        switch (e.Status)
        {
            case HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Conflict
                or HttpStatusCode.UnprocessableEntity:
                return Probleme((int)e.Status, e.Code ?? "service-aval.erreur", e.Detail ?? "La demande a été refusée.", e.Service);
            default:
                logger.LogError(e, "Réponse inattendue du service aval {Service} : {Status} {Code}", e.Service, (int)e.Status, e.Code);
                return Probleme(StatusCodes.Status502BadGateway, "service-aval.reponse-inattendue",
                    "Une erreur est survenue lors de la consultation de vos données. Veuillez réessayer plus tard.", e.Service);
        }
    }

    private static IResult Probleme(int status, string code, string detail, string service) =>
        Results.Problem(
            statusCode: status,
            title: code,
            detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code, ["service"] = service });
}
