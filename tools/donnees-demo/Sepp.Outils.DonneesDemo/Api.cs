using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sepp.Outils.DonneesDemo;

/// <summary>Services appelés directement (ports du compose local, deploy/local/README.md).</summary>
internal enum Service
{
    Referentiels = 5110,
    Affilies = 5111,
    Personnes = 5112,
    PostesRisques = 5113,
    Audit = 5114,
    Integrations = 5115,
    Obligations = 5116,
    Planification = 5117,
    SurveillanceMedicale = 5118,
    Documents = 5119,
    Communications = 5120,
    Reintegration = 5121,
    BffTravailleur = 5201,
}

/// <summary>Réponse HTTP minimale : statut, corps JSON éventuel et texte brut.</summary>
internal sealed record Reponse(HttpStatusCode Statut, JsonNode? Corps, string Texte)
{
    public bool Ok => (int)Statut is >= 200 and < 300;

    /// <summary>Résumé d'une erreur ProblemDetails (code, titre, détail) pour les messages.</summary>
    public string Resume()
    {
        if (Corps is JsonObject o)
        {
            var code = o["code"]?.ToString();
            var detail = o["detail"]?.ToString() ?? o["title"]?.ToString();
            return $"{(int)Statut} {(code is null ? string.Empty : $"[{code}] ")}{detail}".Trim();
        }

        return $"{(int)Statut} {(Texte.Length > 200 ? Texte[..200] : Texte)}".Trim();
    }
}

/// <summary>Échec d'une action de l'amorçage (message destiné à l'opérateur).</summary>
internal sealed class EchecDemoException(string message) : Exception(message)
{
}

/// <summary>Client HTTP des services : un jeton par rôle (flux mot de passe, local uniquement), en-tête de motif d'accès.</summary>
internal sealed class Api(string hote, Jetons jetons)
{
    /// <summary>Motif d'accès exigé par les zones médicales hors relation de soin (§3.3) : texte ASCII, données de démonstration.</summary>
    public const string MotifDemo = "Donnees de demonstration";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);

    public string Hote => hote;

    public Task<Reponse> GetAsync(Service service, string chemin, string utilisateur, bool motif = false) =>
        EnvoyerAsync(HttpMethod.Get, service, chemin, utilisateur, null, motif);

    public Task<Reponse> PostAsync(Service service, string chemin, string utilisateur, object? corps = null, bool motif = false) =>
        EnvoyerAsync(HttpMethod.Post, service, chemin, utilisateur, corps ?? new { }, motif);

    public Task<Reponse> PutAsync(Service service, string chemin, string utilisateur, object corps, bool motif = false) =>
        EnvoyerAsync(HttpMethod.Put, service, chemin, utilisateur, corps, motif);

    /// <summary>Envoi d'un contenu texte brut (import CSV).</summary>
    public async Task<Reponse> PostTexteAsync(Service service, string chemin, string utilisateur, string contenu, string typeContenu)
    {
        using var requete = await CreerAsync(HttpMethod.Post, service, chemin, utilisateur, false);
        requete.Content = new StringContent(contenu, Encoding.UTF8, typeContenu);
        return await ExecuterAsync(requete);
    }

    /// <summary>Appel qui doit réussir ; sinon <see cref="EchecDemoException"/> avec le contexte de l'action.</summary>
    public static Reponse Exiger(Reponse reponse, string action) =>
        reponse.Ok ? reponse : throw new EchecDemoException($"{action} : {reponse.Resume()}");

    private async Task<Reponse> EnvoyerAsync(HttpMethod methode, Service service, string chemin, string utilisateur, object? corps, bool motif)
    {
        using var requete = await CreerAsync(methode, service, chemin, utilisateur, motif);
        if (corps is not null)
        {
            requete.Content = new StringContent(JsonSerializer.Serialize(corps, Json), Encoding.UTF8, "application/json");
        }

        return await ExecuterAsync(requete);
    }

    private async Task<HttpRequestMessage> CreerAsync(HttpMethod methode, Service service, string chemin, string utilisateur, bool motif)
    {
        var url = $"http://{hote}:{(int)service}/{(service == Service.BffTravailleur ? "api/v1/" : "api/v1/")}{chemin.TrimStart('/')}";
        var requete = new HttpRequestMessage(methode, url);
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await jetons.ObtenirAsync(utilisateur));
        requete.Headers.TryAddWithoutValidation("Accept-Language", "fr");
        if (motif)
        {
            requete.Headers.TryAddWithoutValidation("X-Motif-Acces", MotifDemo);
        }

        return requete;
    }

    private static async Task<Reponse> ExecuterAsync(HttpRequestMessage requete)
    {
        try
        {
            using var reponse = await Http.SendAsync(requete);
            var texte = await reponse.Content.ReadAsStringAsync();
            JsonNode? corps = null;
            if (!string.IsNullOrWhiteSpace(texte))
            {
                try
                {
                    corps = JsonNode.Parse(texte);
                }
                catch (JsonException)
                {
                    // Corps non JSON (PDF, texte) : seul le texte brut est conservé.
                }
            }

            return new Reponse(reponse.StatusCode, corps, texte);
        }
        catch (HttpRequestException ex)
        {
            return new Reponse(HttpStatusCode.ServiceUnavailable, null, $"Service injoignable ({requete.RequestUri}) : {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return new Reponse(HttpStatusCode.GatewayTimeout, null, $"Délai dépassé ({requete.RequestUri})");
        }
    }

    /// <summary>Date-heure ISO 8601 encodée pour une chaîne de requête.</summary>
    public static string Q(DateTimeOffset valeur) => Uri.EscapeDataString(valeur.ToString("O", CultureInfo.InvariantCulture));

    public static string Q(DateOnly valeur) => valeur.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>Extensions de lecture des réponses JSON.</summary>
internal static class JsonExtensions
{
    public static Guid Guid(this JsonNode? noeud, string propriete) =>
        System.Guid.Parse(noeud?[propriete]?.GetValue<string>() ?? throw new EchecDemoException($"Réponse sans « {propriete} »."), CultureInfo.InvariantCulture);

    public static string? Texte(this JsonNode? noeud, string propriete) => noeud?[propriete]?.ToString();

    /// <summary>Éléments d'une réponse tableau ou paginée (<c>elements</c>).</summary>
    public static IReadOnlyList<JsonNode> Liste(this Reponse reponse)
    {
        var noeud = reponse.Corps;
        if (noeud is JsonObject o && o["elements"] is JsonArray pagine)
        {
            noeud = pagine;
        }

        return noeud is JsonArray a ? [.. a.Where(n => n is not null).Select(n => n!)] : [];
    }
}

/// <summary>Jetons par utilisateur du realm local (client veilla-dev-cli, flux mot de passe) avec renouvellement avant expiration.</summary>
internal sealed class Jetons(string hote)
{
    public const string MotDePasse = "veilla-dev";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly Dictionary<string, (string Jeton, DateTimeOffset Expire)> _cache = new(StringComparer.Ordinal);

    public async Task<string> ObtenirAsync(string utilisateur)
    {
        if (_cache.TryGetValue(utilisateur, out var entree) && entree.Expire > DateTimeOffset.UtcNow.AddSeconds(45))
        {
            return entree.Jeton;
        }

        using var requete = new HttpRequestMessage(HttpMethod.Post, $"http://{hote}:8180/realms/veilla/protocol/openid-connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "veilla-dev-cli",
                ["username"] = utilisateur,
                ["password"] = MotDePasse,
            }),
        };
        using var reponse = await Http.SendAsync(requete);
        var texte = await reponse.Content.ReadAsStringAsync();
        if (!reponse.IsSuccessStatusCode)
        {
            throw new EchecDemoException($"Jeton de « {utilisateur} » refusé par Keycloak ({(int)reponse.StatusCode}) : l'utilisateur existe-t-il dans le realm veilla ? {texte}");
        }

        var json = JsonNode.Parse(texte)!;
        var jeton = json["access_token"]!.GetValue<string>();
        var duree = json["expires_in"]?.GetValue<int>() ?? 300;
        _cache[utilisateur] = (jeton, DateTimeOffset.UtcNow.AddSeconds(duree));
        return jeton;
    }

    public void Invalider(string utilisateur) => _cache.Remove(utilisateur);

    /// <summary>Claim <c>sub</c> du jeton (identifiant stable de l'utilisateur, référence des ressources de Planification).</summary>
    public async Task<string> SubAsync(string utilisateur)
    {
        var jeton = await ObtenirAsync(utilisateur);
        var charge = jeton.Split('.')[1].Replace('-', '+').Replace('_', '/');
        charge = charge.PadRight(charge.Length + ((4 - (charge.Length % 4)) % 4), '=');
        var json = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(charge)))!;
        return json["sub"]!.GetValue<string>();
    }
}

/// <summary>API d'administration de Keycloak (realm master, <c>admin</c> / <c>admin-local-dev</c>) : poste de développement uniquement.</summary>
internal sealed class KeycloakAdmin(string hote)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private string? _jeton;

    private string Base => $"http://{hote}:8180";

    public async Task<JsonObject?> TrouverUtilisateurAsync(string nom)
    {
        var reponse = await EnvoyerAsync(HttpMethod.Get, $"admin/realms/veilla/users?username={Uri.EscapeDataString(nom)}&exact=true", null);
        var liste = reponse.Liste();
        return liste.Count > 0 ? liste[0] as JsonObject : null;
    }

    /// <summary>Remplace les valeurs d'un attribut multivalué de l'utilisateur (<c>affilie_id</c>, <c>personne_id</c>).</summary>
    public async Task DefinirAttributAsync(string utilisateur, string attribut, IEnumerable<Guid> valeurs)
    {
        var rep = await TrouverUtilisateurAsync(utilisateur)
            ?? throw new EchecDemoException($"Utilisateur Keycloak « {utilisateur} » introuvable.");
        var attributs = rep["attributes"] as JsonObject ?? [];
        attributs[attribut] = new JsonArray([.. valeurs.Select(v => (JsonNode)JsonValue.Create(v.ToString())!)]);
        rep["attributes"] = attributs;
        Api.Exiger(await EnvoyerAsync(HttpMethod.Put, $"admin/realms/veilla/users/{rep["id"]}", rep), $"Mise à jour de l'attribut {attribut} de {utilisateur}");
    }

    /// <summary>Crée l'utilisateur de démonstration s'il manque dans le realm (comptes ajoutés par le lot 0A, #390).</summary>
    public async Task<bool> AssurerUtilisateurAsync(string nom, string roleRealm, string prenom, string nomFamille)
    {
        if (await TrouverUtilisateurAsync(nom) is not null)
        {
            return false;
        }

        var creation = new JsonObject
        {
            ["username"] = nom,
            ["enabled"] = true,
            ["emailVerified"] = true,
            ["firstName"] = prenom,
            ["lastName"] = nomFamille,
            ["email"] = $"{nom}@demo.example",
            ["credentials"] = new JsonArray(new JsonObject { ["type"] = "password", ["value"] = Jetons.MotDePasse, ["temporary"] = false }),
        };
        Api.Exiger(await EnvoyerAsync(HttpMethod.Post, "admin/realms/veilla/users", creation), $"Création de l'utilisateur {nom}");
        var cree = await TrouverUtilisateurAsync(nom) ?? throw new EchecDemoException($"Utilisateur {nom} créé mais introuvable.");
        var role = Api.Exiger(await EnvoyerAsync(HttpMethod.Get, $"admin/realms/veilla/roles/{Uri.EscapeDataString(roleRealm)}", null), $"Lecture du rôle {roleRealm}");
        Api.Exiger(
            await EnvoyerAsync(HttpMethod.Post, $"admin/realms/veilla/users/{cree["id"]}/role-mappings/realm", new JsonArray(role.Corps!.DeepClone())),
            $"Attribution du rôle {roleRealm} à {nom}");
        return true;
    }

    private async Task<string> JetonAsync()
    {
        if (_jeton is not null)
        {
            return _jeton;
        }

        using var requete = new HttpRequestMessage(HttpMethod.Post, $"{Base}/realms/master/protocol/openid-connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = "admin",
                ["password"] = "admin-local-dev",
            }),
        };
        using var reponse = await Http.SendAsync(requete);
        var texte = await reponse.Content.ReadAsStringAsync();
        if (!reponse.IsSuccessStatusCode)
        {
            throw new EchecDemoException($"API d'administration Keycloak inaccessible ({(int)reponse.StatusCode}) : {texte}");
        }

        _jeton = JsonNode.Parse(texte)!["access_token"]!.GetValue<string>();
        return _jeton;
    }

    private async Task<Reponse> EnvoyerAsync(HttpMethod methode, string chemin, JsonNode? corps)
    {
        using var requete = new HttpRequestMessage(methode, $"{Base}/{chemin}");
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await JetonAsync());
        if (corps is not null)
        {
            requete.Content = new StringContent(corps.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var reponse = await Http.SendAsync(requete);
        var texte = await reponse.Content.ReadAsStringAsync();
        JsonNode? json = null;
        if (!string.IsNullOrWhiteSpace(texte))
        {
            try
            {
                json = JsonNode.Parse(texte);
            }
            catch (JsonException)
            {
                // Corps non JSON : message brut conservé.
            }
        }

        return new Reponse(reponse.StatusCode, json, texte);
    }
}
