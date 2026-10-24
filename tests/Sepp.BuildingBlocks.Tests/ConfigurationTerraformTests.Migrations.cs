using System.Text.RegularExpressions;

using Shouldly;

namespace Sepp.BuildingBlocks.Tests;

/// <summary>
/// Migrations de schéma sur Azure (ADR 0010, ARC-50, CTR-16) : un job de migration par service doté d'une base, avec une
/// identité de migration distincte (DDL) et sans secret ; la Container App garde son identité d'exécution (DML).
/// </summary>
public sealed partial class ConfigurationTerraformTests
{
    [Fact]
    public void Chaque_service_dote_d_une_base_a_un_job_de_migration_avec_une_identite_distincte_et_sans_secret()
    {
        var main = Terraform.Lire().Main;

        var job = BlocModule(main, "migration_job");
        job.ShouldContain("\"./modules/containerapp-migration-job\"", Case.Sensitive);
        Regex.IsMatch(job, @"for_each\s*=\s*var\.deploy_container_apps \? \{ for n, s in var\.services : n => s if s\.database \} : \{\}")
            .ShouldBeTrue("Un job de migration par service doté d'une base.");
        Regex.IsMatch(job, @"identity_id\s*=\s*module\.identity\.migration_identities\[each\.key\]\.id")
            .ShouldBeTrue("Le job utilise l'identité de migration du service.");
        job.ShouldNotContain("module.identity.identities[", Case.Sensitive, "Le job ne doit jamais utiliser l'identité d'exécution.");
        job.ShouldNotContain("key_vault_secrets", Case.Sensitive, "Le job de migration ne reçoit aucun secret.");
        job.ShouldNotContain("app_insights_connection_string", Case.Sensitive);
        Regex.IsMatch(job, @"environment_variables\s*=\s*local\.migration_environment\[each\.key\]").ShouldBeTrue();
        Regex.IsMatch(job, @"coalesce\(each\.value\.instance_de, each\.key\)").ShouldBeTrue("Le job exécute l'image du service.");
        Regex.IsMatch(job, @"container_app_environment_id\s*=\s*module\.container_apps_env\[each\.value\.zone\]\.id")
            .ShouldBeTrue("Le job s'exécute dans l'environnement de la zone du service (point privé PostgreSQL de la zone).");

        Regex.IsMatch(BlocModule(main, "identity"), @"migration_services\s*=\s*\[for n, s in var\.services : n if s\.database\]")
            .ShouldBeTrue("Chaque service doté d'une base reçoit une identité de migration.");

        // Environnement du job : identité de migration, mode « migrer puis quitter », rôle d'exécution à doter du DML.
        var environnement = BlocLocal(main, "migration_environment");
        Regex.IsMatch(environnement, @"AZURE_CLIENT_ID\s*=\s*module\.identity\.migration_identities\[n\]\.client_id").ShouldBeTrue();
        Regex.IsMatch(environnement, @"POSTGRES__USER\s*=\s*module\.identity\.migration_identities\[n\]\.name").ShouldBeTrue();
        Regex.IsMatch(environnement, @"DATABASE__MIGRATEANDEXIT\s*=\s*""true""").ShouldBeTrue();
        Regex.IsMatch(environnement, @"DATABASE__RUNTIMEROLE\s*=\s*local\.postgres_environment\[n\]\.POSTGRES__USER")
            .ShouldBeTrue("Le rôle d'exécution qui reçoit le DML est l'identité de la Container App.");
        environnement.ShouldNotContain("key_vault", Case.Insensitive);

        // La Container App garde son identité et son rôle PostgreSQL d'exécution, sans mode de migration.
        var application = BlocModule(main, "container_app");
        Regex.IsMatch(application, @"identity_id\s*=\s*module\.identity\.identities\[each\.key\]\.id").ShouldBeTrue();
        Regex.IsMatch(application, @"environment_variables\s*=\s*local\.app_environment\[each\.key\]").ShouldBeTrue();
        Regex.IsMatch(main, @"POSTGRES__USER\s*=\s*""id-\$\{local\.name_prefix\}-\$\{n\}""").ShouldBeTrue();
        Regex.Count(main, "DATABASE__MIGRATEANDEXIT").ShouldBe(1, "Seul le job de migration migre ; la révision d'exécution sert.");
        BlocLocal(main, "app_environment").ShouldNotContain("migration", Case.Insensitive);
    }

    [Fact]
    public void L_identite_de_migration_n_a_que_le_role_AcrPull_dans_Azure()
    {
        var identite = SansCommentaires(File.ReadAllText(Path.Combine(Terraform.RacineDepot(), "infra", "modules", "identity", "main.tf")));
        identite.ShouldContain("\"id-${var.name_prefix}-${each.key}-migration\"", Case.Sensitive);

        var roles = Regex.Matches(identite, @"resource ""(?<type>[a-z_]+)"" ""(?<nom>[a-z_]+)"" \{")
            .Select(m => (Type: m.Groups["type"].Value, Nom: m.Groups["nom"].Value, Bloc: identite[m.Index..(Terraform.FinAccolade(identite, m.Index + m.Length - 1) + 1)]))
            .Where(r => r.Type != "azurerm_user_assigned_identity" && r.Bloc.Contains("azurerm_user_assigned_identity.migration", StringComparison.Ordinal))
            .ToList();

        roles.ShouldHaveSingleItem("Un seul rôle Azure pour l'identité de migration.");
        roles[0].Bloc.ShouldContain("role_definition_name = \"AcrPull\"", Case.Sensitive, "Aucun accès Key Vault, Service Bus ni stockage pour l'identité de migration.");
    }

    [Fact]
    public void Chaque_service_migre_par_le_mode_commun_avant_toute_verification_de_cle()
    {
        var terraform = Terraform.Lire();
        var programmes = Directory.EnumerateFiles(Path.Combine(Terraform.RacineDepot(), "src", "Services"), "Program.cs", SearchOption.AllDirectories)
            .Where(f => !EstTest(f) && Path.GetFileName(Path.GetDirectoryName(f))!.EndsWith(".Infrastructure", StringComparison.Ordinal))
            .ToList();
        programmes.ShouldNotBeEmpty();
        var problemes = new List<string>();

        foreach (var fichier in programmes)
        {
            var service = NomService(Path.GetFileName(Path.GetDirectoryName(fichier))!);
            var source = File.ReadAllText(fichier);
            if (!source.Contains("DbContext", StringComparison.Ordinal))
            {
                continue;
            }

            var migration = source.IndexOf("MigrerSchemaAsync<", StringComparison.Ordinal);
            if (migration < 0)
            {
                problemes.Add($"{service} : Program.cs n'appelle pas MigrerSchemaAsync (mode « migrer puis quitter » du job de migration).");
                continue;
            }

            if (source.Contains("\"Database:MigrateOnStartup\"", StringComparison.Ordinal) || source.Contains("Database.MigrateAsync", StringComparison.Ordinal))
            {
                problemes.Add($"{service} : migration propre à Program.cs (utiliser MigrerSchemaAsync).");
            }

            // Le job de migration ne reçoit aucun secret : la migration précède les vérifications de clés et le service des requêtes.
            foreach (var suivant in new[] { "app.Services.GetRequiredService", "app.Map", "app.RunAsync" })
            {
                var position = source.IndexOf(suivant, StringComparison.Ordinal);
                if (position >= 0 && position < migration)
                {
                    problemes.Add($"{service} : {suivant} précède MigrerSchemaAsync.");
                }
            }

            if (!terraform.Services.TryGetValue(service, out var definition) || !definition.Database)
            {
                problemes.Add($"{service} : service doté d'un DbContext sans « database = true » dans le catalogue Terraform (pas de job de migration).");
            }
        }

        problemes.ShouldBeEmpty(string.Join(Environment.NewLine, problemes));
    }

    private static string BlocLocal(string main, string nom)
    {
        var marqueur = Regex.Match(main, $@"(?m)^\s*{nom}\s*=\s*\{{");
        marqueur.Success.ShouldBeTrue($"local « {nom} » introuvable dans main.tf.");
        var ouvrante = marqueur.Index + marqueur.Length - 1;
        return main[ouvrante..(Terraform.FinAccolade(main, ouvrante) + 1)];
    }
}
