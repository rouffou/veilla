using System.Globalization;

using Microsoft.AspNetCore.Http;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Domain;

namespace Sepp.BuildingBlocks.Web;

public static class ResultExtensions
{
    /// <summary>Traduit un résultat de cas d'usage en réponse HTTP ; les erreurs deviennent des ProblemDetails (RFC 9457).</summary>
    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult>? onSuccess = null) =>
        result.Match(
            value => onSuccess?.Invoke(value) ?? (value is Unit ? Results.NoContent() : Results.Ok(value)),
            ToProblem);

    public static IResult ToProblem(Error error)
    {
        var status = error.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ErrorKind.Unprocessable => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status500InternalServerError,
        };
        return Results.Problem(
            statusCode: status,
            title: error.Code,
            detail: error.Message,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    /// <summary>
    /// Langue de la réponse : paramètre <c>langue</c> (fr, nl, de, en), sinon en-tête Accept-Language, sinon français (NF-40, NF-41).
    /// </summary>
    public static Language ResolveLanguage(this HttpContext context, string? langue)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(langue))
        {
            candidates.Add(langue);
        }

        candidates.AddRange(context.Request.GetTypedHeaders().AcceptLanguage
            .OrderByDescending(l => l.Quality ?? 1)
            .Select(l => l.Value.Value ?? string.Empty));

        foreach (var candidate in candidates)
        {
            var code = candidate.Split('-')[0].ToUpperInvariant();
            if (Enum.TryParse<Language>(code, ignoreCase: true, out var language))
            {
                return language;
            }
        }

        return Language.Fr;
    }

    public static DateOnly TodayInBelgium(this TimeProvider timeProvider)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone).DateTime);
    }

    public static string Invariant(this decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
