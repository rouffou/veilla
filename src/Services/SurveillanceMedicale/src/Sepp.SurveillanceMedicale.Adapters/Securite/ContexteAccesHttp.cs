using Microsoft.AspNetCore.Http;

using Sepp.SurveillanceMedicale.Application;

namespace Sepp.SurveillanceMedicale.Adapters.Securite;

/// <summary>
/// Contexte d'accès lu dans la requête. Le motif (accès hors relation de soin, §3.3) est transmis dans l'en-tête
/// <c>X-Motif-Acces</c> — jamais dans l'URL — encodé en pourcentage (RFC 3986) s'il contient des caractères non ASCII ;
/// le bris de glace par <c>X-Bris-De-Glace: true</c>. Claims des externes : <c>affilie_id</c>, <c>personne_id</c>.
/// </summary>
internal sealed class ContexteAccesHttp(IHttpContextAccessor accessor) : IContexteAcces
{
    public const string EnTeteMotif = "X-Motif-Acces";
    public const string EnTeteBrisDeGlace = "X-Bris-De-Glace";

    private HttpContext? Http => accessor.HttpContext;

    public string? Motif
    {
        get
        {
            var brut = Http?.Request.Headers[EnTeteMotif].ToString();
            if (string.IsNullOrWhiteSpace(brut))
            {
                return null;
            }

            try
            {
                return Uri.UnescapeDataString(brut).Trim();
            }
            catch (UriFormatException)
            {
                return brut.Trim();
            }
        }
    }

    public bool BrisDeGlace => bool.TryParse(Http?.Request.Headers[EnTeteBrisDeGlace].ToString(), out var bris) && bris;

    public Guid? AffilieIdJeton => Claim("affilie_id");

    public Guid? PersonneIdJeton => Claim("personne_id");

    private Guid? Claim(string type) => Guid.TryParse(Http?.User.FindFirst(type)?.Value, out var id) ? id : null;
}
