using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Sepp.Bff.Travailleur.Aval;

/// <summary>Le service aval a répondu par une erreur (ProblemDetails relu pour être retraduit au portail).</summary>
public sealed class ErreurAvalException(string service, HttpStatusCode status, string? code, string? detail)
    : Exception($"{service} a répondu {(int)status} ({code ?? "sans code"}).")
{
    public string Service { get; } = service;

    public HttpStatusCode Status { get; } = status;

    public string? Code { get; } = code;

    public string? Detail { get; } = detail;
}

/// <summary>Le service aval est injoignable, trop lent ou son disjoncteur est ouvert (ARC-30) : le portail reçoit un 503.</summary>
public sealed class ServiceIndisponibleException(string service, Exception? inner = null)
    : Exception($"Le service {service} est indisponible.", inner)
{
    public string Service { get; } = service;
}

/// <summary>
/// Appels HTTP typés vers un service aval : désérialisation JSON (énumérations en chaînes) et traduction des
/// échecs en <see cref="ErreurAvalException"/> ou <see cref="ServiceIndisponibleException"/>. Aucune règle métier.
/// </summary>
public abstract class ClientAval(HttpClient http, string service)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    protected async Task<T> GetAsync<T>(string uri, CancellationToken ct)
    {
        using var response = await EnvoyerAsync(() => http.GetAsync(uri, ct));
        return await LireAsync<T>(response, ct);
    }

    protected async Task<T> PostAsync<T>(string uri, object corps, CancellationToken ct)
    {
        using var response = await EnvoyerAsync(() => http.PostAsJsonAsync(uri, corps, Json, ct));
        return await LireAsync<T>(response, ct);
    }

    /// <summary>POST dont la réponse de succès n'a pas de corps utile (204).</summary>
    protected async Task PostSansReponseAsync(string uri, CancellationToken ct)
    {
        using var response = await EnvoyerAsync(() => http.PostAsync(uri, null, ct));
    }

    /// <summary>Contenu binaire (PDF) et nom de fichier annoncé par <c>Content-Disposition</c>.</summary>
    protected async Task<(byte[] Contenu, string? NomFichier)> GetOctetsAsync(string uri, CancellationToken ct)
    {
        using var response = await EnvoyerAsync(() => http.GetAsync(uri, ct));
        var contenu = await response.Content.ReadAsByteArrayAsync(ct);
        var disposition = response.Content.Headers.ContentDisposition;
        return (contenu, disposition?.FileNameStar ?? disposition?.FileName?.Trim('"'));
    }

    private async Task<HttpResponseMessage> EnvoyerAsync(Func<Task<HttpResponseMessage>> envoi)
    {
        HttpResponseMessage response;
        try
        {
            response = await envoi();
        }
        catch (Exception e) when (e is HttpRequestException or TimeoutRejectedException or BrokenCircuitException
                                       or TaskCanceledException { InnerException: TimeoutException })
        {
            throw new ServiceIndisponibleException(service, e);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout)
            {
                throw new ServiceIndisponibleException(service);
            }

            var (code, detail) = await LireProblemeAsync(response);
            throw new ErreurAvalException(service, response.StatusCode, code, detail);
        }
    }

    private async Task<T> LireAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, ct)
                   ?? throw new ErreurAvalException(service, HttpStatusCode.BadGateway, "service-aval.reponse-vide", null);
        }
        catch (JsonException e)
        {
            throw new ErreurAvalException(service, HttpStatusCode.BadGateway, "service-aval.reponse-invalide", e.Message);
        }
    }

    private static async Task<(string? Code, string? Detail)> LireProblemeAsync(HttpResponseMessage response)
    {
        try
        {
            var probleme = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (probleme.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            string? Lire(string nom) => probleme.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            return (Lire("code") ?? Lire("title"), Lire("detail"));
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException)
        {
            return (null, null);
        }
    }
}
