using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using Sepp.Documents.Application;

namespace Sepp.Documents.Adapters.Api;

/// <summary>
/// Traduit <see cref="CleRacineIndisponibleException"/> (version de la clé racine supprimée, purgée, désactivée ou accès
/// retiré, #377) en <b>503</b> avec le code <c>document.cle-racine-indisponible</c>.
/// </summary>
/// <remarks>
/// <para>
/// 503 plutôt que 500 : la panne n'est ni un défaut du code ni une altération de l'archive, mais l'indisponibilité d'une
/// dépendance d'exploitation (la version de la clé dans le coffre) ; l'archive redevient lisible, sans changement de code,
/// dès que la version est restaurée ou le droit rétabli. Le statut 503 la distingue aussi des 500 inattendus dans la
/// supervision. Le 409 <c>document.altere</c> reste réservé aux archives effectivement altérées.
/// </para>
/// <para>
/// La réponse ne divulgue ni le kid ni la cause ; le journal d'exploitation (NF-62) porte le kid et la cause, sans aucun
/// contenu de document.
/// </para>
/// </remarks>
public sealed partial class CleRacineIndisponibleExceptionHandler(ILogger<CleRacineIndisponibleExceptionHandler> logger) : IExceptionHandler
{
    public const string Code = "document.cle-racine-indisponible";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not CleRacineIndisponibleException cle)
        {
            return false;
        }

        CleRacineIndisponible(logger, cle.Kid, cle.Cause, cle);
        await Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: Code,
                detail: "La clé de chiffrement de ce document est momentanément indisponible. Le contenu archivé n'est pas en cause : contactez l'exploitation.",
                extensions: new Dictionary<string, object?> { ["code"] = Code })
            .ExecuteAsync(httpContext);
        return true;
    }

    [LoggerMessage(LogLevel.Error,
        "Exploitation (ARC-44, NF-62) : version de clé racine {Kid} inutilisable ({Cause}). Ne pas désactiver ni supprimer une version utilisée par des archives : restaurer la version ou rétablir l'accès.")]
    private static partial void CleRacineIndisponible(ILogger logger, string kid, CauseCleRacineIndisponible cause, Exception exception);
}
