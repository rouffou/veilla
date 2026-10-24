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

    /// <summary>Clés que Terraform fournit à chaque instance du service Documents, quelle que soit sa zone.</summary>
    private static readonly string[] ClesInstanceDocuments =
    [
        "Documents:BlobEndpoint", "Documents:Zones", "Documents:Enveloppe:CleKek", "ServiceBus:SubscriptionName",
    ];

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
        // Documents : une instance par zone (décision #356). Le code est le même ; chaque instance sert sa seule zone
        // (Documents:Zones), écrit dans le stockage de sa zone et enveloppe ses clés de données avec sa clé racine.
        ["documents"] =
        [
            .. Authentification, .. BasePostgres, .. Compte("Documents:CompteTechnique"),
            "Documents:ServicesInternes:Affilies", "Documents:ServicesInternes:Personnes", .. ClesInstanceDocuments,
            .. Adaptateurs("Documents", "Horodatage", "Signature", "SourceLinguistique"),
        ],
        ["documents-medicale"] =
        [
            .. Authentification, .. BasePostgres, .. Compte("Documents:CompteTechnique"),
            "Documents:ServicesInternes:SurveillanceMedicale", .. ClesInstanceDocuments,
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

                if (cle.Contains(":Adaptateurs:", StringComparison.Ordinal) && !SansAdaptateurReel.Contains(service))
                {
                    // Chaque instance du service (même code) doit recevoir l'adaptateur.
                    foreach (var instance in terraform.Instances(service).Where(i => !terraform.CleFournies(i).Contains(Normaliser(cle))))
                    {
                        problemes.Add($"{instance} : {cle} n'est pas injecté par Terraform (adapters du catalogue).");
                    }
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
                // Une instance (instance_de) partage le code, donc l'appsettings, du service d'origine.
                var existe = parService.TryGetValue(definition.InstanceDe ?? service, out var fichier)
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
            if (nom is null)
            {
                continue;
            }

            // L'appsettings porte le nom du service d'origine ; Terraform crée la subscription au nom de chaque instance
            // (instance_de) et le lui injecte (SERVICEBUS__SUBSCRIPTIONNAME = n), ce qui remplace la valeur par défaut.
            if (nom != service)
            {
                ecarts.Add($"{service} : ServiceBus:SubscriptionName = « {nom} » (Terraform crée la subscription au nom du service).");
            }

            foreach (var instance in terraform.Instances(service))
            {
                terraform.Services[instance].SubscribesTo.ShouldNotBeEmpty($"{instance} : subscription configurée mais aucun subscribes_to dans Terraform.");
                terraform.CleFournies(instance).ShouldContain(Normaliser("ServiceBus:SubscriptionName"), $"{instance} : Terraform n'injecte pas SERVICEBUS__SUBSCRIPTIONNAME = nom de l'instance.");
            }
        }

        ecarts.ShouldBeEmpty(string.Join(Environment.NewLine, ecarts));
    }

    [Fact]
    public void Aucun_service_ne_reference_le_coffre_d_une_autre_zone()
    {
        var terraform = Terraform.Lire();
        var ecarts = new List<string>();

        // Le catalogue : un jeu de clés de chiffrement (encryption) ne désigne que la zone du service.
        foreach (var (service, definition) in terraform.Services)
        {
            ecarts.AddRange(definition.EncryptionZones
                .Where(e => e.Zone != definition.Zone)
                .Select(e => $"{service} (zone {definition.Zone}) : encryption {e.Prefixe} désigne le coffre de la zone {e.Zone}."));
        }

        // main.tf : plus de zones additionnelles, et tout accès à un coffre se fait par la zone du service (ou plateforme).
        terraform.Main.ShouldNotContain("additional_key_vault_zones", Case.Sensitive, "Le rôle inter-zones additional_key_vault_zones doit rester supprimé (ARC-04, ARC-06).");
        foreach (Match m in Regex.Matches(terraform.Main, @"module\.keyvault\[(?<i>[^\]]+)\]"))
        {
            var index = m.Groups["i"].Value.Trim();
            if (index is not ("s.zone" or "zone" or "\"platform\""))
            {
                ecarts.Add($"main.tf : module.keyvault[{index}] (seuls s.zone, la zone d'encryption validée et « platform » sont admis).");
            }
        }

        // Module identité : aucun rôle sur le coffre d'une autre zone.
        var identite = File.ReadAllText(Path.Combine(Terraform.RacineDepot(), "infra", "modules", "identity", "main.tf"));
        identite.ShouldNotContain("additional_key_vault_zones", Case.Sensitive);
        identite.ShouldNotContain("key_vault_secrets_user_additional", Case.Sensitive);

        // La validation Terraform refuse encryption vers une autre zone.
        Regex.IsMatch(terraform.Variables, @"zone\s*==\s*s\.zone").ShouldBeTrue("variables.tf doit valider que encryption ne désigne que la zone du service.");

        ecarts.ShouldBeEmpty(string.Join(Environment.NewLine, ecarts));
    }

    [Fact]
    public void Aucune_regle_NSG_n_ouvre_standard_vers_medicale_ou_psychosociale()
    {
        // Analyse textuelle de infra/modules/network/main.tf : les règles sont écrites par zone (variable z), donc on vérifie
        // que l'expression de chaque source n'introduit jamais la zone standard, ni un tag ou une plage large, dans une
        // règle d'autorisation. Une règle qui ajouterait standard comme appelant d'une zone sensible échoue ici.
        var reseau = SansCommentaires(File.ReadAllText(Path.Combine(Terraform.RacineDepot(), "infra", "modules", "network", "main.tf")));
        var problemes = new List<string>();

        reseau.ShouldNotContain("cae-standard", Case.Sensitive);
        reseau.ShouldNotContain("pe-standard", Case.Sensitive);

        var blocs = Regex.Matches(reseau, @"\{[^{}]*\}").Where(b => b.Value.Contains("sources", StringComparison.Ordinal) && b.Value.Contains("access", StringComparison.Ordinal)).ToList();
        blocs.ShouldNotBeEmpty("Aucune règle NSG analysée : le format de infra/modules/network/main.tf a changé.");

        foreach (var bloc in blocs)
        {
            var access = Regex.Match(bloc.Value, "access\\s*=\\s*\"(?<v>[A-Za-z]+)\"").Groups["v"].Value;
            var sources = Regex.Match(bloc.Value, @"sources\s*=\s*(?<v>[^\r\n]+)").Groups["v"].Value.Trim();
            var nom = Regex.Match(bloc.Value, "name\\s*=\\s*\"(?<v>[A-Za-z]+)\"").Groups["v"].Value;

            if (sources.Contains("standard", StringComparison.OrdinalIgnoreCase))
            {
                problemes.Add($"{nom} : la source mentionne la zone standard ({sources}).");
            }

            // Seuls z (la zone de la règle) et platform peuvent apparaître comme préfixes de sous-réseau.
            foreach (Match m in Regex.Matches(sources, @"(?:cae|pe)_prefixes\[(?<i>[^\]]+)\]"))
            {
                if (m.Groups["i"].Value.Trim() is not ("z" or "\"platform\""))
                {
                    problemes.Add($"{nom} : source {m.Value} (seuls z et « platform » sont admis).");
                }
            }

            // values(local.cae_prefixes) englobe standard : réservé à la zone plateforme (z == "platform" ? values(...) : ...).
            if (Regex.IsMatch(sources, @"values\(local\.(?:cae|pe)_prefixes\)")
                && !Regex.IsMatch(sources, @"^z\s*==\s*""platform""\s*\?\s*values\(local\.(?:cae|pe)_prefixes\)"))
            {
                problemes.Add($"{nom} : values(...) des préfixes inclut standard hors de la zone plateforme ({sources}).");
            }

            // Une source générique n'est admise que dans une règle de refus.
            if (access == "Allow" && (sources.Contains("\"*\"", StringComparison.Ordinal) || sources.Contains("VirtualNetwork", StringComparison.Ordinal)))
            {
                problemes.Add($"{nom} : règle Allow avec une source générique ({sources}).");
            }
        }

        problemes.ShouldBeEmpty(string.Join(Environment.NewLine, problemes));
    }

    [Fact]
    public void Chaque_instance_documents_a_son_stockage_dans_sa_zone()
    {
        var terraform = Terraform.Lire();
        var instances = terraform.Instances("documents").ToList();
        instances.ShouldContain("documents");
        instances.ShouldContain("documents-medicale");

        // Une seule instance par zone, et chaque instance est dans la zone qui lui correspond.
        var zones = instances.Select(i => terraform.Services[i].Zone).ToList();
        zones.Distinct().Count().ShouldBe(zones.Count, "Deux instances de Documents dans la même zone partageraient stockage et clé racine.");
        terraform.Services["documents"].Zone.ShouldBe("standard");
        terraform.Services["documents-medicale"].Zone.ShouldBe("medicale");
        terraform.Services["documents-medicale"].InstanceDe.ShouldBe("documents");

        // main.tf : un compte de stockage par zone d'instance, dans le groupe de ressources et derrière le point privé de la zone,
        // avec le seul rôle Blob pour les instances de cette zone ; le point de terminaison injecté est celui du compte de la zone.
        var main = terraform.Main;
        var stockage = BlocModule(main, "storage");
        stockage.ShouldContain("for_each = local.documents_zones");
        Regex.IsMatch(stockage, @"resource_group_name\s*=\s*azurerm_resource_group\.zone\[each\.value\]\.name").ShouldBeTrue("Le stockage doit être dans le groupe de ressources de la zone.");
        Regex.IsMatch(stockage, @"private_endpoint_subnet_id\s*=\s*module\.network\.subnet_ids\[""pe-\$\{each\.value\}""\]").ShouldBeTrue("Le stockage doit être derrière le point privé pe-<zone>.");
        Regex.IsMatch(stockage, @"containers\s*=\s*\[""documents-\$\{each\.value\}""\]").ShouldBeTrue("Un seul conteneur WORM documents-<zone> par compte.");
        Regex.IsMatch(stockage, @"blob_contributor_principal_ids\s*=.*if s\.zone == each\.value").ShouldBeTrue("Seules les instances de la zone reçoivent le rôle Blob Data Contributor.");
        Regex.IsMatch(stockage, @"name\s*=\s*""stsepp\$\{var\.environment\}\$\{local\.zone_short\[each\.value\]\}\$\{local\.suffix\}""").ShouldBeTrue("Nom attendu : stsepp<env><std|med|psy><suffixe>.");
        Regex.IsMatch(main, @"DOCUMENTS__BLOBENDPOINT\s*=\s*module\.storage\[s\.zone\]\.blob_endpoint").ShouldBeTrue("Chaque instance doit recevoir le compte de sa zone.");
        main.ShouldNotContain("module.storage.blob_endpoint", Case.Sensitive);

        // Longueur maximale du nom (22 caractères avec le plus long environnement, preprod).
        ("stsepp" + "preprod" + "med" + "abcdef").Length.ShouldBeLessThanOrEqualTo(24);

        // La clé racine est créée dans le coffre de chaque zone d'instance.
        Regex.IsMatch(main, @"documents_kek_enabled\s*=\s*contains\(local\.documents_zones, each\.value\)").ShouldBeTrue("Le coffre de chaque zone d'instance Documents doit porter documents-kek.");
    }

    [Fact]
    public void Les_cles_obligatoires_de_documents_medicale_sont_injectees()
    {
        var terraform = Terraform.Lire();
        var service = "documents-medicale";
        terraform.Services.ShouldContainKey(service);
        var fournies = terraform.CleFournies(service);

        var manquantes = ClesObligatoires[service].Where(c => !fournies.Contains(Normaliser(c))).ToList();
        manquantes.ShouldBeEmpty("Clés obligatoires de documents-medicale non fournies par Terraform : " + string.Join(", ", manquantes));

        // Valeurs injectées par main.tf pour une instance : zone servie, clé racine, stockage et subscription propres.
        foreach (var variable in new[] { "DOCUMENTS__ZONES", "DOCUMENTS__ENVELOPPE__CLEKEK", "DOCUMENTS__BLOBENDPOINT", "SERVICEBUS__SUBSCRIPTIONNAME" })
        {
            terraform.Main.ShouldContain(variable, Case.Sensitive, $"main.tf n'injecte pas {variable}.");
        }

        Regex.IsMatch(terraform.Main, @"DOCUMENTS__ZONES\s*=\s*s\.zone").ShouldBeTrue("Documents:Zones doit valoir la zone de l'instance.");
        Regex.IsMatch(terraform.Main, @"DOCUMENTS__ENVELOPPE__CLEKEK\s*=\s*module\.keyvault\[s\.zone\]\.documents_kek_uri").ShouldBeTrue("La clé racine est celle du coffre de la zone de l'instance.");
        Regex.IsMatch(terraform.Main, @"SERVICEBUS__SUBSCRIPTIONNAME\s*=\s*n\b").ShouldBeTrue("La subscription porte le nom de l'instance.");
        Regex.IsMatch(terraform.Main, @"coalesce\(each\.value\.instance_de, each\.key\)").ShouldBeTrue("L'image d'une instance est celle du service d'origine.");
        Regex.IsMatch(terraform.Main, @"""\$\{s\.instance_de\}\.\$\{n\}"" => \{ topic = s\.instance_de").ShouldBeTrue("Une instance publie sur le topic du service d'origine (émetteur partagé).");

        // L'instance médicale n'appelle ni Affiliés ni Personnes (NSG de la zone médicale), et n'a pas de topic propre.
        terraform.Services[service].InternalServices.ShouldBe(new[] { "Documents__ServicesInternes__SurveillanceMedicale" });
        terraform.Services[service].CompteTechnique.ShouldBe("Documents__CompteTechnique");
        terraform.Services["documents"].InternalServices.ShouldNotContain("Documents__ServicesInternes__SurveillanceMedicale");
        terraform.Services[service].PublishesEvents.ShouldBeFalse();
        terraform.Services[service].SubscribesTo.ShouldBe(new[] { "surveillance-medicale" });
        terraform.Services["documents"].SubjectsOf("surveillance-medicale").ShouldBe(new[] { "surveillance-medicale.decision-emise.v1" });
        terraform.Services[service].SubjectsOf("surveillance-medicale").Count.ShouldBe(3);

        // Aucun secret Documents__Chiffrement__* : les clés racines sont des clés de coffre, plus des secrets exportables.
        terraform.Variables.ShouldNotContain("Documents__Chiffrement", Case.Sensitive);

        // Identifiant client du compte technique renseigné dans chaque environnement.
        var tfvars = Directory.EnumerateFiles(Path.Combine(Terraform.RacineDepot(), "infra", "environments"), "terraform.tfvars", SearchOption.AllDirectories).ToList();
        tfvars.ShouldNotBeEmpty();
        foreach (var fichier in tfvars)
        {
            Regex.IsMatch(File.ReadAllText(fichier), @"(?m)^\s*documents-medicale\s*=\s*""[0-9a-fA-F-]{36}""").ShouldBeTrue($"{fichier} : technical_account_client_ids doit contenir documents-medicale.");
        }
    }

    private static string BlocModule(string main, string nom)
    {
        var debut = main.IndexOf($"module \"{nom}\" {{", StringComparison.Ordinal);
        debut.ShouldBeGreaterThanOrEqualTo(0, $"module \"{nom}\" introuvable dans main.tf.");
        var ouvrante = main.IndexOf('{', debut);
        return main[ouvrante..(Terraform.FinAccolade(main, ouvrante) + 1)];
    }

    private static string SansCommentaires(string contenu) => Terraform.SansCommentaires(contenu);

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

        private Terraform(string main, string variables, Dictionary<string, ServiceTf> services)
        {
            _main = main;
            Variables = variables;
            Services = services;
        }

        public IReadOnlyDictionary<string, ServiceTf> Services { get; }

        /// <summary>Contenu de <c>infra/main.tf</c> sans commentaires.</summary>
        public string Main => _main;

        /// <summary>Contenu de <c>infra/variables.tf</c> sans commentaires.</summary>
        public string Variables { get; }

        /// <summary>Instances déployées du code d'un service : lui-même et les services qui en sont <c>instance_de</c>.</summary>
        public IEnumerable<string> Instances(string service) =>
            Services.Where(s => (s.Value.InstanceDe ?? s.Key) == service).Select(s => s.Key);

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
            return new Terraform(main, variables, AnalyserCatalogue(variables));
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

            // Variables communes à tous les services abonnés : la subscription porte le nom du service ou de l'instance.
            Ajouter(fournies, s.SubscribesTo.Count > 0 && Regex.IsMatch(_main, @"SERVICEBUS__SUBSCRIPTIONNAME\s*=\s*n\b"), "ServiceBus__SubscriptionName");

            // Instances du service Documents : une par zone, avec stockage, zone servie et clé racine propres.
            if ((s.InstanceDe ?? service) == "documents")
            {
                Ajouter(fournies, _main.Contains("DOCUMENTS__BLOBENDPOINT", StringComparison.Ordinal) && Fusionne("documents_environment"), "Documents__BlobEndpoint");
                Ajouter(fournies, _main.Contains("DOCUMENTS__ZONES", StringComparison.Ordinal) && Fusionne("documents_environment"), "Documents__Zones");
                Ajouter(fournies, _main.Contains("DOCUMENTS__ENVELOPPE__CLEKEK", StringComparison.Ordinal) && Fusionne("documents_environment"), "Documents__Enveloppe__CleKek");
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

        public static string SansCommentaires(string contenu) => Regex.Replace(contenu, @"#[^\r\n]*", string.Empty);

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
                    KeyVaultSecrets: Cles(bloc, @"key_vault_secrets\s*=\s*\{(?<v>[^}]*)\}"),
                    InstanceDe: Regex.Match(bloc, "instance_de\\s*=\\s*\"(?<v>[^\"]+)\"") is { Success: true } i ? i.Groups["v"].Value : null,
                    PublishesEvents: !Regex.IsMatch(bloc, @"publishes_events\s*=\s*false"),
                    EncryptionZones: Regex.Match(bloc, @"encryption\s*=\s*\{(?<v>[^}]*)\}") is { Success: true } e
                        ? [.. Regex.Matches(e.Groups["v"].Value, "(?:^|[\\s,])\"?(?<k>[A-Za-z][A-Za-z0-9_-]*)\"?\\s*=\\s*\"(?<z>[^\"]*)\"").Select(v => (v.Groups["k"].Value, v.Groups["z"].Value))]
                        : [],
                    SubjectFilters: Regex.Match(bloc, @"subject_filters\s*=\s*\{(?<v>[^}]*)\}") is { Success: true } f
                        ? Regex.Matches(f.Groups["v"].Value, @"""?(?<k>[A-Za-z][A-Za-z0-9_-]*)""?\s*=\s*\[(?<l>[^\]]*)\]")
                            .ToDictionary(v => v.Groups["k"].Value, v => Regex.Matches(v.Groups["l"].Value, "\"([^\"]+)\"").Select(x => x.Groups[1].Value).ToList(), StringComparer.Ordinal)
                        : []);
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

        public static int FinAccolade(string texte, int ouvrante)
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
        List<string> KeyVaultSecrets,
        string? InstanceDe,
        bool PublishesEvents,
        List<(string Prefixe, string Zone)> EncryptionZones,
        Dictionary<string, List<string>> SubjectFilters)
    {
        /// <summary>Sujets acceptés par la subscription du service sur un topic (vide = tous les messages).</summary>
        public List<string> SubjectsOf(string topic) => SubjectFilters.TryGetValue(topic, out var sujets) ? sujets : [];
    }
}
