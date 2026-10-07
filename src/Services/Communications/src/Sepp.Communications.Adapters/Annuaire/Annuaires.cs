using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

using Sepp.BuildingBlocks.Domain;
using Sepp.Communications.Application;
using Sepp.Communications.Domain.Messages;

namespace Sepp.Communications.Adapters.Annuaire;

/// <summary>Dirigeant interne à alerter (configuration <c>Communications:Annuaire:Dirigeants:&lt;zone&gt;</c>).</summary>
public sealed class DirigeantConfigure
{
    public Guid Id { get; set; }

    public string? Nom { get; set; }

    public string? Email { get; set; }

    public Language Langue { get; set; } = Language.Fr;
}

/// <summary>
/// Dirigeants à alerter par zone : CPMT dirigeant (<c>medicale</c>), CPAP dirigeant (<c>psychosociale</c>). Aucune API
/// n'expose aujourd'hui les membres d'un rôle du fournisseur d'identité : la liste est configurée (voir README).
/// </summary>
public sealed class OptionsAnnuaire
{
    public Dictionary<string, List<DirigeantConfigure>> Dirigeants { get; set; } = new(StringComparer.Ordinal);

    internal IReadOnlyList<Destinataire> Pour(string zone) =>
        Dirigeants.TryGetValue(zone, out var liste) ? [.. liste.Select(Convertir)] : [];

    internal Destinataire? Trouver(Guid id) =>
        Dirigeants.Values.SelectMany(l => l).Where(d => d.Id == id).Select(Convertir).FirstOrDefault();

    private static Destinataire Convertir(DirigeantConfigure d) =>
        new(TypeDestinataire.Interne, d.Id, d.Langue, d.Nom, d.Email, null, null, null, null, PortailActif: true, CanalPrefere: Canal.Email);
}

/// <summary>
/// Annuaire réel : <c>GET /api/v1/personnes/{id}</c> (Personnes : langue, e-mail, téléphone, adresse, canal préféré) et
/// <c>GET /api/v1/affilies/{id}</c> (Affiliés : langue, numéro BCE pour l'eBox Entreprise, personne de contact, adresse de
/// l'unité d'établissement), avec le jeton du compte technique (rôle <c>communications</c>). Un destinataire introuvable est
/// signalé par <c>null</c> ; toute autre erreur lève une exception (reprise du message).
/// </summary>
/// <remarks>
/// API manquantes, documentées dans le README du service : existence d'un compte portail (le portail est supposé actif), identifiant
/// eBox Citoyen du travailleur, adresse de correspondance et canal préféré de l'affilié (l'adresse de la première unité
/// d'établissement et la personne de contact sont utilisées), membres des rôles dirigeants du fournisseur d'identité
/// (liste configurée).
/// </remarks>
internal sealed class AnnuaireHttp(IHttpClientFactory fabrique, OptionsAnnuaire options) : IAnnuaireDestinataires
{
    public async Task<Destinataire?> ObtenirAsync(TypeDestinataire type, Guid id, CancellationToken cancellationToken) => type switch
    {
        TypeDestinataire.Personne => await PersonneAsync(id, cancellationToken),
        TypeDestinataire.Affilie => await AffilieAsync(id, cancellationToken),
        _ => options.Trouver(id),
    };

    public Task<IReadOnlyList<Destinataire>> ResoudreDirigeantsAsync(string zone, CancellationToken cancellationToken) =>
        Task.FromResult(options.Pour(zone));

    private async Task<Destinataire?> PersonneAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await LireAsync(ClientsHttp.Personnes, $"api/v1/personnes/{id}", cancellationToken) is not { } p)
        {
            return null;
        }

        var nom = string.Join(' ', new[] { Texte(p, "prenom"), Texte(p, "nom") }.Where(t => !string.IsNullOrWhiteSpace(t)));
        var adresse = p.TryGetProperty("adresse", out var a) && a.ValueKind == JsonValueKind.Object ? Adresse(nom, a, "pays") : null;
        return new Destinataire(
            TypeDestinataire.Personne, id, Langue(p), nom, Texte(p, "email"), Texte(p, "telephone"), adresse, EBoxEntreprise: null, EBoxCitoyen: null,
            PortailActif: true, Canaux.Depuis(Texte(p, "canalPrefere")));
    }

    private async Task<Destinataire?> AffilieAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await LireAsync(ClientsHttp.Affilies, $"api/v1/affilies/{id}", cancellationToken) is not { } a || !a.TryGetProperty("fiche", out var fiche))
        {
            return null;
        }

        var denomination = Texte(fiche, "denomination");
        var bce = new string((Texte(fiche, "numeroBce") ?? string.Empty).Where(char.IsDigit).ToArray());
        string? email = null;
        string? telephone = null;
        if (a.TryGetProperty("contacts", out var contacts) && contacts.ValueKind == JsonValueKind.Array)
        {
            var contact = contacts.EnumerateArray().FirstOrDefault(c => string.Equals(Texte(c, "role"), "PersonneDeContact", StringComparison.OrdinalIgnoreCase));
            if (contact.ValueKind == JsonValueKind.Object)
            {
                email = Texte(contact, "email");
                telephone = Texte(contact, "telephone");
            }
        }

        AdressePostale? adresse = null;
        if (a.TryGetProperty("unitesEtablissement", out var unites) && unites.ValueKind == JsonValueKind.Array)
        {
            var unite = unites.EnumerateArray().FirstOrDefault();
            if (unite.ValueKind == JsonValueKind.Object && unite.TryGetProperty("adresse", out var adresseUnite) && adresseUnite.ValueKind == JsonValueKind.Object)
            {
                adresse = Adresse(denomination ?? string.Empty, adresseUnite, "codePays");
            }
        }

        return new Destinataire(
            TypeDestinataire.Affilie, id, Langue(fiche), denomination, email, telephone, adresse, bce.Length == 10 ? bce : null, EBoxCitoyen: null,
            PortailActif: true, CanalPrefere: null);
    }

    private async Task<JsonElement?> LireAsync(string client, string chemin, CancellationToken cancellationToken)
    {
        var http = fabrique.CreateClient(client);
        if (http.BaseAddress is null)
        {
            throw new InvalidOperationException($"Adresse du service interne non configurée ({client}, Communications:ServicesInternes).");
        }

        using var reponse = await http.GetAsync(new Uri(chemin, UriKind.Relative), cancellationToken);
        if (reponse.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        reponse.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await reponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    private static AdressePostale? Adresse(string nom, JsonElement a, string proprietePays)
    {
        var rue = Texte(a, "rue");
        var codePostal = Texte(a, "codePostal");
        var localite = Texte(a, "localite");
        return string.IsNullOrWhiteSpace(rue) || string.IsNullOrWhiteSpace(codePostal) || string.IsNullOrWhiteSpace(localite)
            ? null
            : new AdressePostale(nom, rue, Texte(a, "numero") ?? string.Empty, Texte(a, "boite"), codePostal, localite, Texte(a, proprietePays) ?? "BE");
    }

    private static Language Langue(JsonElement element) =>
        Enum.TryParse<Language>(Texte(element, "langue"), ignoreCase: true, out var langue) ? langue : Language.Fr;

    private static string? Texte(JsonElement element, string propriete) =>
        element.TryGetProperty(propriete, out var valeur) && valeur.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? valeur.ValueKind == JsonValueKind.String ? valeur.GetString() : valeur.GetRawText()
            : null;
}

/// <summary>
/// Annuaire fictif (développement, tests) : un destinataire joignable par tous les canaux, dérivé de son identifiant, sauf
/// s'il est défini explicitement (<see cref="Definir"/>) ou déclaré inconnu (<see cref="Supprimer"/>). Données fictives
/// uniquement (NF-14).
/// </summary>
public sealed class AnnuaireSimule(OptionsAnnuaire options) : IAnnuaireDestinataires
{
    private readonly ConcurrentDictionary<(TypeDestinataire, Guid), Destinataire?> _surcharges = new();

    public void Definir(Destinataire destinataire) => _surcharges[(destinataire.Type, destinataire.Id)] = destinataire;

    /// <summary>Le destinataire devient inconnu du service propriétaire.</summary>
    public void Supprimer(TypeDestinataire type, Guid id) => _surcharges[(type, id)] = null;

    public Task<Destinataire?> ObtenirAsync(TypeDestinataire type, Guid id, CancellationToken cancellationToken)
    {
        if (type == TypeDestinataire.Interne)
        {
            return Task.FromResult(options.Trouver(id));
        }

        if (_surcharges.TryGetValue((type, id), out var surcharge))
        {
            return Task.FromResult(surcharge);
        }

        var suffixe = id.ToString("N")[^12..];
        var adresse = new AdressePostale($"Destinataire fictif {suffixe}", "Rue de l'Exemple", "1", null, "1000", "Bruxelles", "BE");
        return Task.FromResult<Destinataire?>(type == TypeDestinataire.Personne
            ? new Destinataire(type, id, Language.Fr, $"Personne fictive {suffixe}", $"personne-{suffixe}@exemple.test", "+3225550100", adresse, null, null, true, Canal.Email)
            : new Destinataire(type, id, Language.Fr, $"Affilié fictif {suffixe}", $"contact-{suffixe}@exemple.test", "+3225550101", adresse, "0123456789", null, true, null));
    }

    public Task<IReadOnlyList<Destinataire>> ResoudreDirigeantsAsync(string zone, CancellationToken cancellationToken) =>
        Task.FromResult(options.Pour(zone));
}
