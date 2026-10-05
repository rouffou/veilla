using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Sepp.BuildingBlocks.Web;

/// <summary>
/// Traduit en 409 les conflits détectés par la base au moment de l'écriture : requêtes concurrentes
/// sur une clé unique (ex. deux réceptions simultanées de la même déclaration DIMONA) et verrou
/// optimiste (DAT-03). Le client peut relire puis réessayer ; aucun détail technique n'est exposé.
/// </summary>
public sealed class ConflictExceptionHandler : IExceptionHandler
{
    private const string UniqueViolation = "23505";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (code, detail) = exception switch
        {
            DbUpdateConcurrencyException =>
                ("conflit.concurrence", "La ressource a été modifiée entre-temps. Relisez-la puis réessayez."),
            DbUpdateException { InnerException: PostgresException { SqlState: UniqueViolation } } =>
                ("conflit.doublon", "Une ressource identique vient d'être enregistrée par une autre requête."),
            _ => (null, null),
        };

        if (code is null)
        {
            return false;
        }

        await ResultExtensions.ToProblem(Application.Error.Conflict(code, detail!)).ExecuteAsync(httpContext);
        return true;
    }
}
