using System.Text.Json;
using System.Text.RegularExpressions;

using Shouldly;

namespace Sepp.BuildingBlocks.Tests;

/// <summary>
/// Garde-fou de dérive entre le code et Terraform (ARC-40, ARC-44, CTR-05, CTR-16) : chaque clé de configuration
/// obligatoire lue au démarrage d'un service doit être fournie par <c>infra/main.tf</c> et <c>infra/variables.tf</c>
/// (variables d'environnement, références Key Vault). La liste des clés obligatoires est tenue ici, par service ; le
/// test échoue aussi quand le code lit une section de configuration que personne n'a classée (nouvelle clé oubliée dans
/// Terraform) ou quand un <c>appsettings.json</c> choisit un adaptateur « Reel » par défaut. Analyse textuelle,
/// sans Azure ni Terraform.
/// </summary>
public sealed partial class ConfigurationTerraformTests
{
    private static readonly string[] Authentification = ["Authentication:Authority", "Authentication:Audience"];

    private static readonly string[] BasePostgres = ["Postgres:Host", "Postgres:Database", "Postgres:User"];

    private static string[] Compte(string prefixe) =>
        [$"{prefixe}:TokenEndpoint", $"{prefixe}:ClientId", $"{prefixe}:ClientSecret", $"{prefixe}:Scope"];

    private static string[] Chiffrement(string prefixe) => [$"{prefixe}:CurrentKeyId", $"{prefixe}:Keys"];

    private static string[] Adaptateurs(string section, params string[] ports) => [.. ports.Select(p => $"{section}:Adaptateurs:{p}")];

    /// <summary>
    /// Clés obligatoires au démarrage ou à l'usage de chaque service déployé (hors valeurs par défaut d'appsettings.json),
    /// relevées dans <c>DependencyInjection.cs</c> et <c>Program.cs</c> de chaque service. Nom = catalogue Terraform.
    /// </summary>
    private static readonly Dictionary<string, string[]> ClesObligatoires = new(StringComparer.Ordinal)
    {
        ["affilies"] = [.. Authentification, .. BasePostgres, .. Compte("Affilies:CompteTechnique"), "Affilies:ServicesInternes:Integrations"],
        ["audit"] = [.. Authentification, .. BasePostgres],
        ["communications"] =
        [
            .. Authentification, .. BasePostgres, .. Compte("Communications:CompteTechnique"),
            "Communications:ServicesInternes:Affilies", "Communications:ServicesInternes:Personnes",
            .. Adaptateurs("Communications", "Annuaire", "Portail", "Email", "Sms", "Courrier", "RecommandeElectronique", "EBoxEntreprise", "EBoxCitoyen"),
        ],
        ["documents"] =
        [
            .. Authentification, .. BasePostgres, .. Compte("Documents:CompteTechnique"),
            "Documents:ServicesInternes:Affilies", "Documents:ServicesInternes:Personnes", "Documents:ServicesInternes:SurveillanceMedicale", "Documents:BlobEndpoint",
            .. Chiffrement("Documents:Chiffrement:standard:Encryption"),
            .. Chiffrement("Documents:Chiffrement:medicale:Encryption"),
            .. Chiffrement("Documents:Chiffrement:psychosociale:Encryption"),
            .. Adaptateurs("Documents", "Horodatage", "Signature", "SourceLinguistique"),
        ],
        ["integrations"] =
        [
            .. Authentification, .. BasePostgres, .. Compte("Integrations:Personnes"), "Integrations:Personnes:BaseAddress",
            .. Chiffrement("Encryption"),
            .. Adaptateurs("Integrations", "Bce", "Dimona", "RegistreNational", "Incapacites"),
        ],
        ["obligations"] = [.. Authentification, .. BasePostgres],
        ["personnes"] = [.. Authentification, .. BasePostgres, .. Chiffrement("Encryption"), "BlindIndex:Key"],
        ["planification"] = [.. Authentification, .. BasePostgres, .. Adaptateurs("Planification", "OutilRh", "AgendaExterne")],
        ["postes-risques"] = [.. Authentification, .. BasePostgres],
        ["referentiels"] = [.. Authentification, .. BasePostgres],
        ["reintegration"] = [.. Authentification, .. BasePostgres, .. Chiffrement("ZoneMedicale:Encryption"), .. Adaptateurs("Reintegration", "EHealth")],
        ["surveillance-medicale"] = [.. Authentification, .. BasePostgres, .. Chiffrement("ZoneMedicale:Encryption")],
        ["bff-employeur"] = [.. Authentification, "Sepp:Services:Affilies", "Sepp:Services:Personnes", "Sepp:Services:Postes_Risques", "Sepp:Services:Obligations"],
        ["bff-travailleur"] = [.. Authentification, "Sepp:Services:Planification", "Sepp:Services:Surveillance_Medicale", "Sepp:Services:Obligations", "Sepp:Services:Documents"],
    };

    /// <summary>
    /// Services dont les adaptateurs « Reel » n'existent pas : le choix reste « Simulateur » (appsettings.json), le service
    /// refuse toute autre valeur, et Terraform n'a rien à injecter.
    /// </summary>
    private static readonly HashSet<string> SansAdaptateurReel = new(StringComparer.Ordinal) { "surveillance-medicale" };

    /// <summary>
    /// Racines de configuration lues par le code et leur traitement : « Terraform » (obligatoire, vérifié ci-dessus),
    /// « Defaut » (valeur par défaut d'appsettings.json ou du code, facultative) ou « Local » (développement et tests).
    /// </summary>
    private static readonly Dictionary<string, string> RacinesClassees = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Authentication"] = "Terraform",
        ["Postgres"] = "Terraform",
        ["Encryption"] = "Terraform",
        ["BlindIndex"] = "Terraform",
        ["ZoneMedicale"] = "Terraform",
        ["Sepp"] = "Terraform",
        ["Affilies"] = "Terraform",
        ["Communications"] = "Terraform",
        ["Documents"] = "Terraform",
        ["Integrations"] = "Terraform",
        ["Planification"] = "Terraform",
        ["Reintegration"] = "Defaut",
        ["ServiceBus"] = "Terraform",
        ["ServicesAval"] = "Defaut",
        ["ConnectionStrings"] = "Local",
        ["Database"] = "Defaut",
        ["Outbox"] = "Defaut",
        ["Cors"] = "Defaut",
        ["Audit"] = "Defaut",
        ["Obligations"] = "Defaut",
        ["SurveillanceMedicale"] = "Defaut",
        ["ListesNominatives"] = "Defaut",
        ["RequireHttpsMetadata"] = "Defaut",
        ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = "Terraform",
        ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "Local",
    };

    [Fact]
    public void Chaque_cle_obligatoire_de_chaque_service_est_fournie_par_terraform()
    {
        var terraform = Terraform.Lire();
        var manquantes = new List<string>();

        foreach (var (service, cles) in ClesObligatoires)
        {
            terraform.Services.ShouldContainKey(service, $"Le service '{service}' est absent du catalogue Terraform (infra/variables.tf).");
            var fournies = terraform.CleFournies(service);
            manquantes.AddRange(cles
                .Where(c => !fournies.Contains(Normaliser(c)))
                .Select(c => $"{service} : {c}"));
        }

        manquantes.ShouldBeEmpty(
            "Clés de configuration obligatoires non fournies par Terraform (infra/main.tf, infra/variables.tf) :" +
            Environment.NewLine + string.Join(Environment.NewLine, manquantes));
    }

    [Fact]
    public void Chaque_service_deploye_porte_ses_cles_obligatoires_dans_le_test()
    {
        // Un service ajouté au catalogue sans liste de clés ici échappe au garde-fou.
        var nonCouverts = Terraform.Lire().Services.Keys
            .Except(ClesObligatoires.Keys, StringComparer.Ordinal)
            .Except(ServicesSansCode, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        nonCouverts.ShouldBeEmpty(
            "Services du catalogue Terraform sans clés obligatoires déclarées dans ConfigurationTerraformTests : " + string.Join(", ", nonCouverts));
    }

    /// <summary>Services du catalogue Terraform dont le code n'existe pas encore dans le dépôt (aucune configuration à vérifier).</summary>
    private static readonly string[] ServicesSansCode =
        ["bff-interne", "identite", "prestations", "prevention", "psychosocial", "reporting"];

    [Fact]
    public void Chaque_adaptateur_declare_dans_appsettings_est_injecte_par_terraform_et_aucun_ne_vaut_Reel_par_defaut()
    {
        var terraform = Terraform.Lire();
        var problemes = new List<string>();

        foreach (var (service, fichier) in AppSettings())
        {
            using var document = JsonDocument.Parse(File.ReadAllText(fichier), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            foreach (var (cle, valeur) in Aplatir(document.RootElement, string.Empty))
            {
                if (valeur == "Reel")
                {
                    problemes.Add($"{service} : {cle} vaut « Reel » dans appsettings.json (défaut sûr = Simulateur ; Reel est imposé par Terraform en production).");
                }

                if (cle.Contains(":Adaptateurs:", StringComparison.Ordinal) && !SansAdaptateurReel.Contains(service)
                    && !terraform.CleFournies(service).Contains(Normaliser(cle)))
                {
                    problemes.Add($"{service} : {cle} n'est pas injecté par Terraform (adapters du catalogue).");
                }
            }
        }

        problemes.ShouldBeEmpty(string.Join(Environment.NewLine, problemes));
    }

    [Fact]
    public void Chaque_adaptateur_du_catalogue_terraform_existe_dans_appsettings()
    {
        var terraform = Terraform.Lire();
        var parService = AppSettings().ToDictionary(a => a.Service, a => a.Fichier, StringComparer.Ordinal);
        var inconnus = new List<string>();

        foreach (var (service, definition) in terraform.Services)
        {
            foreach (var adaptateur in definition.Adapters)
            {
                var cle = adaptateur.Replace("__", ":", StringComparison.Ordinal);
                var existe = parService.TryGetValue(service, out var fichier)
                    && Aplatir(JsonDocument.Parse(File.ReadAllText(fichier), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }).RootElement, string.Empty).Any(c => string.Equals(c.Cle, cle, StringComparison.OrdinalIgnoreCase));
                if (!existe)
                {
                    inconnus.Add($"{service} : {adaptateur}");
                }
            }
        }

        inconnus.ShouldBeEmpty("Adaptateurs déclarés dans Terraform mais absents d'appsettings.json : " + string.Join(", ", inconnus));
    }

    [Fact]
    public void Le_nom_de_subscription_de_chaque_service_est_son_nom_dans_le_catalogue()
    {
        var terraform = Terraform.Lire();
        var ecarts = new List<string>();

        foreach (var (service, fichier) in AppSettings())
        {
            using var document = JsonDocument.Parse(File.ReadAllText(fichier), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            var nom = Aplatir(document.RootElement, string.Empty).FirstOrDefault(c => c.Cle == "ServiceBus:SubscriptionName").Valeur;
            if (nom is not null && nom != service)
            {
                ecarts.Add($"{service} : ServiceBus:SubscriptionName = « {nom} » (Terraform crée la subscription au nom du service).");
            }

            if (nom is not null)
            {
                terraform.Services[service].SubscribesTo.ShouldNotBeEmpty($"{service} : subscription configurée mais aucun subscribes_to dans Terraform.");
            }
        }

        ecarts.ShouldBeEmpty(string.Join(Environment.NewLine, ecarts));
    }

    [Fact]
    public void Toute_cle_de_configuration_lue_par_le_code_a_ete_classee()
    {
        var inconnues = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var fichier in Directory.EnumerateFiles(Path.Combine(Terraform.RacineDepot(), "src"), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !EstTest(f)))
        {
            var source = File.ReadAllText(fichier);
            foreach (var cle in LecturesConfiguration(source))
            {
                var racine = cle.Split(':', 2)[0];
                if (!RacinesClassees.ContainsKey(racine))
                {
                    inconnues.Add($"{racine} ({Path.GetFileName(fichier)} : {cle})");
                }
            }
        }

        inconnues.ShouldBeEmpty(
            "Clés de configuration lues par le code sans classement dans ConfigurationTerraformTests " +
            "(les ajouter à ClesObligatoires et à Terraform si elles sont obligatoires, sinon à RacinesClassees) : " + string.Join(", ", inconnues));
    }

    private static bool EstTest(string chemin) =>
        chemin.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || chemin.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || chemin.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static IEnumerable<string> LecturesConfiguration(string source)
    {
        foreach (Match m in LectureLitterale().Matches(source))
        {
            yield return m.Groups["cle"].Value;
        }

        foreach (Match m in ChaineConnexion().Matches(source))
        {
            yield return "ConnectionStrings:" + m.Groups["cle"].Value;
        }
    }

    [GeneratedRegex("""[Cc]onfiguration(?:\.(?:GetSection|GetValue|GetValue<[^>]+>)\(|\[)\s*\$?"(?<cle>[A-Za-z_][A-Za-z0-9_:]*)""")]
    private static partial Regex LectureLitterale();

    [GeneratedRegex("""GetConnectionString\(\s*"(?<cle>[A-Za-z0-9_]+)"\s*\)""")]
    private static partial Regex ChaineConnexion();

    private static IEnumerable<(string Service, string Fichier)> AppSettings()
    {
        var racine = Terraform.RacineDepot();
        foreach (var fichier in Directory.EnumerateFiles(Path.Combine(racine, "src"), "appsettings.json", SearchOption.AllDirectories).Where(f => !EstTest(f)))
        {
            var projet = Path.GetFileName(Path.GetDirectoryName(fichier))!;
            yield return (NomService(projet), fichier);
        }
    }

    /// <summary>« Sepp.PostesRisques.Infrastructure » -> « postes-risques » ; « Sepp.Bff.Employeur » -> « bff-employeur ».</summary>
    private static string NomService(string projet)
    {
        var morceaux = projet.Split('.').Skip(1).Where(p => p != "Infrastructure");
        var nom = string.Join('-', morceaux.Select(p => Regex.Replace(p, "(?<!^)([A-Z])", "-$1").ToLowerInvariant()));
        return nom;
    }

    private static IEnumerable<(string Cle, string? Valeur)> Aplatir(JsonElement element, string prefixe)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var propriete in element.EnumerateObject())
                {
                    foreach (var enfant in Aplatir(propriete.Value, prefixe.Length == 0 ? propriete.Name : $"{prefixe}:{propriete.Name}"))
                    {
                        yield return enfant;
                    }
                }

                break;
            case JsonValueKind.Array:
                break;
            default:
                yield return (prefixe, element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString());
                break;
        }
    }

    /// <summary>Forme canonique d'une clé : « A__B » (variable d'environnement) et « A:B » (configuration) se confondent, sans casse.</summary>
    private static string Normaliser(string cle) => cle.Replace("__", ":", StringComparison.Ordinal).ToLowerInvariant();

    /// <summary>Contenu utile de <c>infra/variables.tf</c> (catalogue des services) et <c>infra/main.tf</c> (injection).</summary>
    private sealed class Terraform
    {
        private readonly string _main;

        private Terraform(string main, Dictionary<string, ServiceTf> services)
        {
            _main = main;
            Services = services;
        }

        public IReadOnlyDictionary<string, ServiceTf> Services { get; }

        public static string RacineDepot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "infra", "main.tf")) && File.Exists(Path.Combine(dir.FullName, "Sepp.slnx")))
                {
                    return dir.FullName;
                }
            }

            throw new InvalidOperationException("Racine du dépôt introuvable (infra/main.tf, Sepp.slnx).");
        }

        public static Terraform Lire()
        {
            var racine = RacineDepot();
            var main = SansCommentaires(File.ReadAllText(Path.Combine(racine, "infra", "main.tf")));
            var variables = SansCommentaires(File.ReadAllText(Path.Combine(racine, "infra", "variables.tf")));
            return new Terraform(main, AnalyserCatalogue(variables));
        }

        /// <summary>Clés (forme canonique) que Terraform fournit au service : variables d'environnement et secrets Key Vault.</summary>
        public HashSet<string> CleFournies(string service)
        {
            var fournies = new HashSet<string>(StringComparer.Ordinal);
            if (!Services.TryGetValue(service, out var s))
            {
                return fournies;
            }

            // Injection commune, vérifiée dans main.tf : chaque clé doit y figurer et le local être fusionné dans l'environnement.
            Ajouter(fournies, Fusionne("authentication_environment"), "Authentication__Authority", "Authentication__Audience");
            if (s.Database)
            {
                Ajouter(fournies, _main.Contains("postgres_environment", StringComparison.Ordinal) && _main.Contains("try(local.postgres_environment[n]", StringComparison.Ordinal),
                    "POSTGRES__HOST", "POSTGRES__DATABASE", "POSTGRES__USER");
            }

            if (s.Zone == "platform")
            {
                foreach (var cible in Services.Where(c => c.Value.Zone != "platform").Select(c => c.Key))
                {
                    Ajouter(fournies, _main.Contains("SEPP__SERVICES__", StringComparison.Ordinal) && Fusionne("bff_environment"), $"Sepp__Services__{cible.Replace('-', '_')}");
                }
            }

            if (service == "documents")
            {
                Ajouter(fournies, _main.Contains("DOCUMENTS__BLOBENDPOINT", StringComparison.Ordinal) && Fusionne("documents_environment"), "Documents__BlobEndpoint");
            }

            // Clés propres au service déclarées dans le catalogue.
            Ajouter(fournies, Fusionne("adapters_environment"), [.. s.Adapters]);
            Ajouter(fournies, Fusionne("internal_services_environment"), [.. s.InternalServices]);
            if (s.CompteTechnique is { } compte)
            {
                Ajouter(fournies, Fusionne("technical_account_environment") && ContientTous("__TokenEndpoint", "__ClientId", "__Scope"),
                    $"{compte}__TokenEndpoint", $"{compte}__ClientId", $"{compte}__Scope");
                Ajouter(fournies, ContientTous("--clientsecret", "service_key_vault_secrets"), $"{compte}__ClientSecret");
            }

            foreach (var prefixe in s.Encryption)
            {
                Ajouter(fournies, Fusionne("encryption_environment") && ContientTous("__CurrentKeyId"), $"{prefixe}__CurrentKeyId");
                Ajouter(fournies, ContientTous("--keys--", "service_key_vault_secrets"), $"{prefixe}__Keys");
            }

            // key_vault_secrets du catalogue : variable = nom du secret en majuscules, « - » -> « _ ».
            foreach (var secret in s.KeyVaultSecrets)
            {
                fournies.Add(Normaliser(secret.Replace('-', '_')));
            }

            return fournies;
        }

        private bool ContientTous(params string[] fragments) => fragments.All(f => _main.Contains(f, StringComparison.Ordinal));

        private bool Fusionne(string local) =>
            _main.Contains($"{local} ", StringComparison.Ordinal) && Regex.IsMatch(_main, $@"local\.{local}(\[n\])?\s*[,\)]");

        private static void Ajouter(HashSet<string> fournies, bool injecte, params string[] cles)
        {
            if (!injecte)
            {
                return;
            }

            foreach (var cle in cles)
            {
                fournies.Add(Normaliser(cle));
            }
        }

        private static string SansCommentaires(string contenu) => Regex.Replace(contenu, @"#[^\r\n]*", string.Empty);

        private static Dictionary<string, ServiceTf> AnalyserCatalogue(string variables)
        {
            var debut = variables.IndexOf("variable \"services\"", StringComparison.Ordinal);
            debut.ShouldBeGreaterThanOrEqualTo(0, "variable \"services\" introuvable.");
            var marqueur = Regex.Match(variables[debut..], @"default\s*=\s*\{");
            marqueur.Success.ShouldBeTrue("default = { ... } de variable \"services\" introuvable.");
            var ouvrante = debut + marqueur.Index + marqueur.Length - 1;
            var catalogue = variables[(ouvrante + 1)..FinAccolade(variables, ouvrante)];

            var services = new Dictionary<string, ServiceTf>(StringComparer.Ordinal);
            foreach (Match entree in Regex.Matches(catalogue, "\"(?<nom>[a-z][a-z0-9-]*)\"\\s*=\\s*\\{"))
            {
                var ouvre = entree.Index + entree.Length - 1;
                var bloc = catalogue[ouvre..(FinAccolade(catalogue, ouvre) + 1)];
                var nom = entree.Groups["nom"].Value;
                if (services.ContainsKey(nom) || !Regex.IsMatch(bloc, @"\bzone\s*="))
                {
                    continue;
                }

                services[nom] = new ServiceTf(
                    Zone: Regex.Match(bloc, "zone\\s*=\\s*\"(?<z>[a-z]+)\"").Groups["z"].Value,
                    Database: Regex.IsMatch(bloc, @"database\s*=\s*true"),
                    SubscribesTo: Valeurs(bloc, @"subscribes_to\s*=\s*\[(?<v>[^\]]*)\]"),
                    Adapters: Valeurs(bloc, @"adapters\s*=\s*\[(?<v>[^\]]*)\]"),
                    CompteTechnique: Regex.Match(bloc, "compte_technique\\s*=\\s*\"(?<v>[^\"]+)\"") is { Success: true } c ? c.Groups["v"].Value : null,
                    InternalServices: Cles(bloc, @"internal_services\s*=\s*\{(?<v>[^}]*)\}"),
                    Encryption: Cles(bloc, @"encryption\s*=\s*\{(?<v>[^}]*)\}"),
                    KeyVaultSecrets: Cles(bloc, @"key_vault_secrets\s*=\s*\{(?<v>[^}]*)\}"));
            }

            return services;
        }

        private static List<string> Valeurs(string bloc, string motif) =>
            Regex.Match(bloc, motif) is { Success: true } m
                ? [.. Regex.Matches(m.Groups["v"].Value, "\"([^\"]+)\"").Select(v => v.Groups[1].Value)]
                : [];

        /// <summary>Clés d'une table HCL <c>{ cle = "valeur" }</c>, entre guillemets ou non.</summary>
        private static List<string> Cles(string bloc, string motif) =>
            Regex.Match(bloc, motif) is { Success: true } m
                ? [.. Regex.Matches(m.Groups["v"].Value, "(?:^|[\\s,])\"?(?<k>[A-Za-z][A-Za-z0-9_-]*)\"?\\s*=\\s*\"[^\"]*\"").Select(v => v.Groups["k"].Value)]
                : [];

        private static int FinAccolade(string texte, int ouvrante)
        {
            var profondeur = 0;
            for (var i = ouvrante; i < texte.Length; i++)
            {
                profondeur += texte[i] switch { '{' => 1, '}' => -1, _ => 0 };
                if (profondeur == 0)
                {
                    return i;
                }
            }

            throw new InvalidOperationException("Accolade fermante introuvable dans variables.tf.");
        }
    }

    private sealed record ServiceTf(
        string Zone,
        bool Database,
        List<string> SubscribesTo,
        List<string> Adapters,
        string? CompteTechnique,
        List<string> InternalServices,
        List<string> Encryption,
        List<string> KeyVaultSecrets);
}
