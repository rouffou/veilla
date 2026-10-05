using Microsoft.Net.Http.Headers;

using Sepp.BuildingBlocks.Web;

namespace Sepp.Bff.Employeur.Aval;

/// <summary>
/// ARC-40 : le BFF n'a pas d'identité propre vis-à-vis des services ; il recopie le jeton de l'utilisateur
/// (en-tête Authorization) et l'identifiant de corrélation (ARC-47) sur chaque appel aval. Chaque service
/// revalide le jeton (émetteur, audience) et applique lui-même le périmètre de l'affilié (défense en profondeur).
/// </summary>
public sealed class PropagationJetonHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (accessor.HttpContext is { } context)
        {
            var authorization = context.Request.Headers[HeaderNames.Authorization].ToString();
            if (!string.IsNullOrWhiteSpace(authorization))
            {
                request.Headers.Remove(HeaderNames.Authorization);
                request.Headers.TryAddWithoutValidation(HeaderNames.Authorization, authorization);
            }

            var correlation = context.Response.Headers[CorrelationIdMiddleware.Header].ToString();
            if (string.IsNullOrWhiteSpace(correlation))
            {
                correlation = context.TraceIdentifier;
            }

            request.Headers.Remove(CorrelationIdMiddleware.Header);
            request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.Header, correlation);

            var langue = context.Request.Headers[HeaderNames.AcceptLanguage].ToString();
            if (!string.IsNullOrWhiteSpace(langue) && !request.Headers.Contains(HeaderNames.AcceptLanguage))
            {
                request.Headers.TryAddWithoutValidation(HeaderNames.AcceptLanguage, langue);
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
