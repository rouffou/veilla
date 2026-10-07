using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Npgsql;

using Sepp.BuildingBlocks.Web;

namespace Sepp.Planification.Adapters.Api;

/// <summary>
/// Traduit en 409 la violation de la contrainte d'exclusion <c>ex_occupation_ressource_chevauchement</c> (SQLSTATE 23P01) :
/// deux requêtes concurrentes ont tenté d'occuper la même ressource sur des périodes qui se chevauchent. Le socle ne
/// traduit que les clés uniques et le verrou optimiste ; la contrainte d'exclusion est propre à ce service.
/// </summary>
public sealed class ChevauchementExceptionHandler : IExceptionHandler
{
    private const string ExclusionViolation = "23P01";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DbUpdateException { InnerException: PostgresException { SqlState: ExclusionViolation } })
        {
            return false;
        }

        await ResultExtensions.ToProblem(
                BuildingBlocks.Application.Error.Conflict("creneau.chevauchement", "Une ressource de ce créneau est déjà occupée sur cette période."))
            .ExecuteAsync(httpContext);
        return true;
    }
}
