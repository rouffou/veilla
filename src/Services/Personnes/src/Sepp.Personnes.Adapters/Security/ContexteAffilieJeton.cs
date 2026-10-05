using Microsoft.AspNetCore.Http;

using Sepp.Personnes.Application;

namespace Sepp.Personnes.Adapters.Security;

/// <summary>Lit l'affilié de l'utilisateur externe dans le claim <c>affilie_id</c> du jeton (ADR 0005).</summary>
internal sealed class ContexteAffilieJeton(IHttpContextAccessor accessor) : IContexteAffilie
{
    public const string ClaimType = "affilie_id";

    public Guid? AffilieId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst(ClaimType)?.Value, out var id) ? id : null;
}
