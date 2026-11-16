using System.Text.RegularExpressions;

using Shouldly;

namespace Sepp.BuildingBlocks.Tests;

/// <summary>
/// Jobs de rechiffrement et de vérification des champs sur Azure (#474, ADR 0012) : <c>rech-&lt;service&gt;</c> et
/// <c>verif-&lt;service&gt;</c> pour chaque service qui chiffre des champs, avec l'identité d'exécution du service, déclenchés
/// à la main. Les variables <c>RECHIFFREMENT__*</c> ne figurent que dans ces jobs, jamais dans une révision de service.
/// </summary>
public sealed partial class ConfigurationTerraformTests
{
    private static readonly string[] ServicesChiffrants = ["personnes", "integrations", "surveillance-medicale", "reintegration"];

    [Fact]
    public void Aucune_variable_RECHIFFREMENT_n_est_injectee_dans_une_revision_de_service()
    {
        // Un service qui reçoit RECHIFFREMENT__EXECUTERPUISQUITTER (ou VERIFIERSEULEMENT) rechiffre puis quitte au démarrage :
        // la Container App redémarrerait en boucle et ne servirait plus rien. Ces variables n'existent que dans les jobs.
        var racine = Terraform.RacineDepot();
        var main = Terraform.Lire().Main;

        // main.tf : seuls les deux locals des jobs peuvent les nommer.
        var sansJobs = main
            .Replace(BlocLocal(main, "rechiffrement_environment"), string.Empty, StringComparison.Ordinal)
            .Replace(BlocLocal(main, "verification_environment"), string.Empty, StringComparison.Ordinal);
        Regex.IsMatch(sansJobs, "RECHIFFREMENT(__|:)", RegexOptions.IgnoreCase)
            .ShouldBeFalse("RECHIFFREMENT__* ne peut figurer que dans les locals rechiffrement_environment et verification_environment de infra/main.tf.");

        // Ni les révisions (app_environment, bloc container_app), ni les locals fusionnés dans app_environment ne les référencent.
        BlocLocal(main, "app_environment").ShouldNotContain("rechiffrement", Case.Insensitive);
        BlocLocal(main, "app_environment").ShouldNotContain("verification", Case.Insensitive);
        var application = BlocModule(main, "container_app");
        application.ShouldNotContain("rechiffrement", Case.Insensitive);
        application.ShouldNotContain("verification", Case.Insensitive);
        Regex.IsMatch(application, @"environment_variables\s*=\s*local\.app_environment\[each\.key\]").ShouldBeTrue();

        // Les deux locals ne sont référencés que par leur propre job.
        Regex.Count(main, @"local\.rechiffrement_environment\b").ShouldBe(1);
        Regex.Count(main, @"local\.verification_environment\b").ShouldBe(1);
        BlocModule(main, "rechiffrement_job").ShouldContain("local.rechiffrement_environment[each.key]", Case.Sensitive);
        BlocModule(main, "verification_job").ShouldContain("local.verification_environment[each.key]", Case.Sensitive);

        // Aucun autre fichier Terraform (modules, environnements, variables, tfvars) ne les porte, hors commentaires.
        var autres = Directory.EnumerateFiles(Path.Combine(racine, "infra"), "*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".tf", StringComparison.Ordinal) || f.EndsWith(".tfvars", StringComparison.Ordinal))
                && !f.Contains($"{Path.DirectorySeparatorChar}.terraform{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !string.Equals(Path.GetFullPath(f), Path.GetFullPath(Path.Combine(racine, "infra", "main.tf")), StringComparison.Ordinal))
            .Where(f => Regex.IsMatch(Terraform.SansCommentaires(File.ReadAllText(f)), "RECHIFFREMENT(__|:)", RegexOptions.IgnoreCase))
            .Select(f => Path.GetRelativePath(racine, f))
            .ToList();
        autres.ShouldBeEmpty("RECHIFFREMENT__* ne doit apparaître dans aucun autre fichier Terraform : " + string.Join(", ", autres));
    }

    [Fact]
    public void Chaque_service_qui_chiffre_a_un_job_rech_et_un_job_verif_avec_son_identite_d_execution()
    {
        var terraform = Terraform.Lire();
        var main = terraform.Main;
        terraform.Services.Where(s => s.Value.Encryption.Count > 0).Select(s => s.Key).ShouldBe(ServicesChiffrants, ignoreOrder: true);
        Regex.IsMatch(main, @"rechiffrement_services\s*=\s*\{\s*for n, s in local\.services : n => s if length\(s\.encryption\) > 0\s*\}")
            .ShouldBeTrue("Un couple de jobs par service dont encryption n'est pas vide.");

        foreach (var (module, prefixe, usage) in new[] { ("rechiffrement_job", "rech", "rechiffrement"), ("verification_job", "verif", "verification") })
        {
            var job = BlocModule(main, module);
            job.ShouldContain("\"./modules/containerapp-rechiffrement-job\"", Case.Sensitive);
            Regex.IsMatch(job, @"for_each\s*=\s*var\.deploy_container_apps \? local\.rechiffrement_services : \{\}").ShouldBeTrue($"{module} : un job par service qui chiffre.");
            Regex.IsMatch(job, $@"name\s*=\s*""{prefixe}-\$\{{each\.key\}}""").ShouldBeTrue($"{module} : nom {prefixe}-<service>.");
            Regex.IsMatch(job, @"identity_id\s*=\s*module\.identity\.identities\[each\.key\]\.id")
                .ShouldBeTrue($"{module} : identité d'exécution du service (elle déballe les DEK), jamais une identité élargie.");
            job.ShouldNotContain("migration_identities", Case.Sensitive);
            Regex.IsMatch(job, @"key_vault_secrets\s*=\s*local\.service_key_vault_secrets\[each\.key\]")
                .ShouldBeTrue($"{module} : mêmes secrets --deks-- et --keys-- que la Container App.");
            Regex.IsMatch(job, @"coalesce\(each\.value\.instance_de, each\.key\)").ShouldBeTrue($"{module} : image du service.");
            Regex.IsMatch(job, @"container_app_environment_id\s*=\s*module\.container_apps_env\[each\.value\.zone\]\.id")
                .ShouldBeTrue($"{module} : environnement de la zone du service (ARC-06).");
            Regex.IsMatch(job, @"replica_timeout_in_seconds\s*=\s*var\.rechiffrement_replica_timeout_seconds").ShouldBeTrue($"{module} : délai paramétrable.");
            job.ShouldContain($"usage = \"{usage}\"", Case.Sensitive);
        }

        // Variables : configuration du service, plus UN seul mode, jamais les deux.
        var rechiffrement = BlocLocal(main, "rechiffrement_environment");
        Regex.IsMatch(rechiffrement, @"merge\(local\.app_environment\[n\],").ShouldBeTrue();
        Regex.IsMatch(rechiffrement, @"RECHIFFREMENT__EXECUTERPUISQUITTER\s*=\s*""true""").ShouldBeTrue();
        rechiffrement.ShouldNotContain("VERIFIERSEULEMENT", Case.Insensitive);
        var verification = BlocLocal(main, "verification_environment");
        Regex.IsMatch(verification, @"merge\(local\.app_environment\[n\],").ShouldBeTrue();
        Regex.IsMatch(verification, @"RECHIFFREMENT__VERIFIERSEULEMENT\s*=\s*""true""").ShouldBeTrue();
        verification.ShouldNotContain("EXECUTERPUISQUITTER", Case.Insensitive);
        // Ni l'identité de migration, ni le mode de migration : l'exécution est celle du service.
        (rechiffrement + verification).ShouldNotContain("migration", Case.Insensitive);
        (rechiffrement + verification).ShouldNotContain("MIGRATEANDEXIT", Case.Insensitive);

        Regex.IsMatch(terraform.Variables, @"variable ""rechiffrement_replica_timeout_seconds""[^}]*default\s*=\s*14400").ShouldBeTrue("Délai par défaut de 4 h, plus long que la migration (1 800 s).");
    }

    [Fact]
    public void Les_jobs_de_rechiffrement_sont_manuels_a_une_execution_sans_nouvelle_tentative_et_sans_declencheur()
    {
        var module = SansCommentaires(File.ReadAllText(Path.Combine(Terraform.RacineDepot(), "infra", "modules", "containerapp-rechiffrement-job", "main.tf")));
        Regex.IsMatch(module, @"manual_trigger_config\s*\{\s*parallelism\s*=\s*1\s*replica_completion_count\s*=\s*1\s*\}").ShouldBeTrue("Une réplique par exécution.");
        Regex.IsMatch(module, @"replica_retry_limit\s*=\s*0").ShouldBeTrue("Aucune nouvelle tentative automatique.");
        module.ShouldNotContain("schedule_trigger_config", Case.Sensitive, "Aucun déclenchement planifié.");
        module.ShouldNotContain("event_trigger_config", Case.Sensitive, "Aucun déclenchement par événement.");
        Regex.IsMatch(module, @"identity\s*=\s*var\.identity_id").ShouldBeTrue("Secrets Key Vault lus avec l'identité du job (celle du service).");
        Regex.IsMatch(module, @"ignore_changes\s*=\s*\[template\[0\]\.container\[0\]\.image\]").ShouldBeTrue("Image mise à jour par la chaîne de déploiement.");
        module.ShouldNotContain("RECHIFFREMENT__", Case.Insensitive, "Le mode est choisi par l'appelant, jamais fixé dans le module.");
    }

    [Fact]
    public void Le_deploiement_ne_declenche_jamais_un_job_de_rechiffrement_ou_de_verification()
    {
        var racine = Terraform.RacineDepot();
        var deploy = SansCommentairesYaml(File.ReadAllText(Path.Combine(racine, ".github", "workflows", "deploy.yml")));
        deploy.ShouldNotContain("rech-", Case.Sensitive);
        deploy.ShouldNotContain("verif-", Case.Sensitive);
        deploy.ShouldNotContain("job start", Case.Sensitive);
        deploy.ShouldNotContain("verification-cles", Case.Sensitive, "La vérification des clés se lance à la demande, pas depuis le déploiement.");

        // Le script de déploiement met l'image des jobs à jour, sans jamais les démarrer (seul mig-<service> est démarré).
        var script = File.ReadAllText(Path.Combine(racine, "tools", "deploy", "deployer-service.sh"));
        Regex.Count(script, @"job start").ShouldBe(1, "Un seul démarrage de job : la migration.");
        script.ShouldContain("job start -g \"$GROUPE\" -n \"$JOB\"", Case.Sensitive);
        script.ShouldContain("rech-${SERVICE}", Case.Sensitive);
        script.IndexOf("rech-${SERVICE}", StringComparison.Ordinal).ShouldBeGreaterThan(script.IndexOf("for palier in", StringComparison.Ordinal), "Image des jobs alignée après le passage à 100 %.");
        script.ShouldContain("verif-${SERVICE}", Case.Sensitive);
    }

    [Fact]
    public void La_verification_des_cles_couvre_tous_les_services_qui_chiffrent_et_exige_un_environnement_protege()
    {
        var racine = Terraform.RacineDepot();
        var workflows = Path.Combine(racine, ".github", "workflows");
        var verification = SansCommentairesYaml(File.ReadAllText(Path.Combine(workflows, "verification-cles.yml")));
        var controle = SansCommentairesYaml(File.ReadAllText(Path.Combine(workflows, "controle-cles.yml")));

        // La liste des services vérifiés suit le catalogue Terraform : un nouveau service qui chiffre doit y être ajouté.
        var liste = Regex.Match(verification, @"(?m)^\s*SERVICES_CHIFFRANTS:\s*""(?<s>[^""]+)""");
        liste.Success.ShouldBeTrue("SERVICES_CHIFFRANTS absent de verification-cles.yml.");
        liste.Groups["s"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ShouldBe(ServicesChiffrants, ignoreOrder: true,
            "verification-cles.yml doit vérifier exactement les services du catalogue dont encryption n'est pas vide.");

        // Environnement GitHub du même nom que l'environnement cible : règles d'approbation, identité fédérée par environnement.
        Regex.IsMatch(verification, @"environment:\s*\$\{\{\s*inputs\.environnement\s*\}\}").ShouldBeTrue("Environnement protégé.");
        verification.ShouldNotContain("workflow_call", Case.Sensitive, "Jamais appelé depuis une PR : lancement manuel sur main seulement.");
        verification.ShouldNotContain("pull_request", Case.Sensitive);
        verification.ShouldContain("run-name: \"Vérification des clés : ${{ inputs.environnement }}\"", Case.Sensitive);
        verification.ShouldContain("github.ref == 'refs/heads/main'", Case.Sensitive);
        verification.ShouldContain("workflow_dispatch", Case.Sensitive);
        verification.ShouldContain("verifier-rechiffrement.sh", Case.Sensitive);
        verification.ShouldNotContain("rech-", Case.Sensitive, "Le pipeline ne lance que les jobs de vérification, jamais le rechiffrement.");
        verification.ShouldNotContain("EXECUTERPUISQUITTER", Case.Insensitive);

        // Le contrôle de PR détecte la modification des deux variables dans les tfvars et appelle la vérification.
        // Le contrôle de PR ne lit que l'API GitHub : aucun environnement, id-token ni secret, et des scripts venus de la base.
        controle.ShouldNotContain("environment:", Case.Sensitive);
        controle.ShouldNotContain("id-token", Case.Sensitive);
        controle.ShouldNotContain("secrets.", Case.Sensitive);
        controle.ShouldNotContain("secrets:", Case.Sensitive);
        controle.ShouldNotContain("azure/login", Case.Sensitive);
        controle.ShouldNotContain("uses: ./", Case.Sensitive);
        controle.ShouldContain("actions: read", Case.Sensitive);
        controle.ShouldContain("base/tools/deploy/controler-verification-recente.sh", Case.Sensitive);
        controle.ShouldNotContain("pr/tools", Case.Sensitive);
        controle.ShouldContain("ref: ${{ github.event.pull_request.base.sha }}", Case.Sensitive);
        controle.ShouldContain("base/tools/deploy/detecter-modification-cles.sh", Case.Sensitive);
        controle.ShouldContain("merge_group", Case.Sensitive);
        controle.ShouldContain("Statut contrôle des clés", Case.Sensitive);
        var detection = File.ReadAllText(Path.Combine(racine, "tools", "deploy", "detecter-modification-cles.sh"));
        detection.ShouldContain("encryption(_legacy)?_key_ids", Case.Sensitive);
        detection.ShouldContain("infra/environments/*.tfvars", Case.Sensitive);
        detection.ShouldContain("infra/environments/*.tf'", Case.Sensitive);

        // Chaque environnement du dépôt déclare encore les deux variables (le contrôle de PR s'appuie sur elles).
        foreach (var dossier in Directory.EnumerateDirectories(Path.Combine(racine, "infra", "environments")))
        {
            var tfvars = Terraform.SansCommentaires(File.ReadAllText(Path.Combine(dossier, "terraform.tfvars")));
            tfvars.ShouldContain("encryption_key_ids", Case.Sensitive, dossier);
            tfvars.ShouldContain("encryption_legacy_key_ids", Case.Sensitive, dossier);

            // Les valeurs viennent des variables : jamais de liste en dur dans le main.tf de l'environnement (le contrôle suit les tfvars).
            var mainEnvironnement = Terraform.SansCommentaires(File.ReadAllText(Path.Combine(dossier, "main.tf")));
            Regex.IsMatch(mainEnvironnement, @"(?m)^\s*encryption_key_ids\s*=\s*var\.encryption_key_ids\s*$").ShouldBeTrue($"{dossier}/main.tf : encryption_key_ids = var.encryption_key_ids.");
            Regex.IsMatch(mainEnvironnement, @"(?m)^\s*encryption_legacy_key_ids\s*=\s*var\.encryption_legacy_key_ids\s*$").ShouldBeTrue($"{dossier}/main.tf : encryption_legacy_key_ids = var.encryption_legacy_key_ids.");
        }
    }

    /// <summary>Retire les commentaires YAML (<c>#</c> en début de ligne ou précédé d'un espace).</summary>
    private static string SansCommentairesYaml(string contenu) => Regex.Replace(contenu, @"(?m)(^|\s)#[^\r\n]*", "$1");
}
