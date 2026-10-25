namespace Sepp.Bff.Interne.Relais;

/// <summary>
/// Un service joignable par le relais : segment d'URL (nom du service dans le catalogue Terraform), clé de configuration
/// locale (<c>ServicesAval:&lt;Cle&gt;</c>, compose et poste de développement) et clé injectée par Terraform
/// (<c>SEPP__SERVICES__&lt;NOM&gt;</c>, lue comme <c>Sepp:Services:&lt;Nom&gt;</c>).
/// </summary>
public sealed record ServiceRelaye(string Segment, string Cle)
{
    /// <summary>« postes-risques » → « Sepp:Services:Postes_Risques » (main.tf : <c>upper(replace(n, "-", "_"))</c>).</summary>
    public string CleTerraform => $"Sepp:Services:{Segment.Replace('-', '_')}";
}

/// <summary>
/// Liste fermée des services relayés (ADR 0013, ARC-43). Un service absent de cette liste n'a pas de route : le relais
/// répond 404 sans appel aval. Ajouter un service est une décision revue (code, compose, Terraform), jamais une
/// configuration d'environnement.
/// </summary>
public static class ServicesRelayes
{
    public const string Section = "ServicesAval";

    public static readonly IReadOnlyList<ServiceRelaye> Tous =
    [
        new("referentiels", "Referentiels"),
        new("audit", "Audit"),
        new("affilies", "Affilies"),
        new("personnes", "Personnes"),
        new("postes-risques", "PostesRisques"),
        new("obligations", "Obligations"),
        new("integrations", "Integrations"),
        new("surveillance-medicale", "SurveillanceMedicale"),
        new("planification", "Planification"),
        new("documents", "Documents"),
        new("communications", "Communications"),
        new("reintegration", "Reintegration"),
    ];

    /// <summary>
    /// Adresse de chaque service, par segment : <c>ServicesAval:&lt;Cle&gt;</c>, sinon la clé Terraform. Échoue au démarrage
    /// si une adresse manque ou n'est pas une URL http(s) absolue sans chemin, requête ni identifiants.
    /// </summary>
    /// <exception cref="InvalidOperationException">Adresse absente ou invalide.</exception>
    public static IReadOnlyDictionary<string, Uri> Resoudre(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var adresses = new Dictionary<string, Uri>(StringComparer.Ordinal);
        var erreurs = new List<string>();
        foreach (var service in Tous)
        {
            var brute = configuration[$"{Section}:{service.Cle}"];
            if (string.IsNullOrWhiteSpace(brute))
            {
                brute = configuration[service.CleTerraform];
            }

            if (Uri.TryCreate(brute, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
                && uri.AbsolutePath == "/")
            {
                adresses[service.Segment] = new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
            }
            else
            {
                erreurs.Add($"{Section}:{service.Cle}");
            }
        }

        return erreurs.Count == 0
            ? adresses
            : throw new InvalidOperationException(
                "Adresses des services relayés absentes ou invalides (URL http(s) absolue, sans chemin) : " + string.Join(", ", erreurs) + ".");
    }
}
