using System.Text.RegularExpressions;

using Shouldly;

namespace Sepp.BuildingBlocks.Tests;

/// <summary>
/// Bus de développement de l'environnement local (ADR 0004, amendement) : chaque service de <c>deploy/local/compose.yaml</c>
/// doté d'une base publie et consomme par le bus, avec le nom d'abonnement et les filtres de sujets de Terraform
/// (<c>subscribes_to</c>, <c>subject_filters</c> de <c>infra/variables.tf</c>), pour la même sémantique qu'Azure Service Bus.
/// </summary>
public sealed partial class ConfigurationTerraformTests
{
    private const string CleBus = "ConnectionStrings__BusDeveloppement";

    [Fact]
    public void Chaque_service_local_dote_d_une_base_utilise_le_bus_de_developpement()
    {
        var terraform = Terraform.Lire();
        var compose = Compose("compose.yaml");

        var sansBus = compose
            .Where(s => terraform.Services.TryGetValue(s.Key, out var tf) && tf.Database && !s.Value.ContainsKey(CleBus))
            .Select(s => s.Key)
            .ToList();

        sansBus.ShouldBeEmpty($"Services locaux dotés d'une base sans {CleBus} : {string.Join(", ", sansBus)}.");
        compose.Values.Where(e => e.ContainsKey(CleBus)).Select(e => e[CleBus]).Distinct().ShouldHaveSingleItem().ShouldContain("Database=bus");
        File.ReadAllText(Path.Combine(Terraform.RacineDepot(), "deploy", "local", "postgres", "init-databases.sql")).ShouldContain("CREATE DATABASE bus;");
    }

    [Fact]
    public void Les_abonnements_et_filtres_du_bus_local_sont_ceux_de_terraform()
    {
        var terraform = Terraform.Lire();
        var ecarts = new List<string>();

        foreach (var (service, environnement) in Compose("compose.yaml").Where(s => s.Value.ContainsKey(CleBus)))
        {
            // L'instance unique « documents » sert localement les trois zones : elle reçoit aussi les sujets de documents-medicale.
            var attendus = service == "documents"
                ? Fusion(terraform.Services["documents"], terraform.Services["documents-medicale"])
                : terraform.Services[service].SubjectFilters;
            ecarts.AddRange(Ecarts(service, environnement, terraform.Services[service], attendus));
        }

        // Déploiement par zone : documents retrouve exactement le filtre Terraform de la zone standard.
        var parZone = Compose("compose.yaml", "compose.documents-par-zone.yaml");
        ecarts.AddRange(Ecarts("documents (par zone)", parZone["documents"], terraform.Services["documents"], terraform.Services["documents"].SubjectFilters));

        ecarts.ShouldBeEmpty(string.Join(Environment.NewLine, ecarts));
    }

    private static IEnumerable<string> Ecarts(string service, Dictionary<string, string> environnement, ServiceTf terraform, Dictionary<string, List<string>> attendus)
    {
        var abonnement = environnement.GetValueOrDefault("ServiceBus__SubscriptionName");
        var nom = service.Split(' ')[0];
        if (terraform.SubscribesTo.Count > 0 && abonnement != nom)
        {
            yield return $"{service} : ServiceBus__SubscriptionName = « {abonnement} », attendu « {nom} » (subscription Terraform).";
        }

        var filtres = environnement
            .Select(e => (Match: FiltreSujet().Match(e.Key), e.Value))
            .Where(e => e.Match.Success && !string.IsNullOrWhiteSpace(e.Value))
            .GroupBy(e => e.Match.Groups["topic"].Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Value).Order(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);

        foreach (var topic in filtres.Keys.Union(attendus.Keys, StringComparer.Ordinal))
        {
            var local = filtres.GetValueOrDefault(topic) ?? [];
            var tf = (attendus.GetValueOrDefault(topic) ?? []).Order(StringComparer.Ordinal).ToList();
            if (!local.SequenceEqual(tf, StringComparer.Ordinal))
            {
                yield return $"{service} : filtre {topic} = [{string.Join(", ", local)}], Terraform = [{string.Join(", ", tf)}].";
            }
        }
    }

    private static Dictionary<string, List<string>> Fusion(ServiceTf a, ServiceTf b) =>
        a.SubjectFilters.Keys.Union(b.SubjectFilters.Keys, StringComparer.Ordinal).ToDictionary(
            t => t,
            t => a.SubjectsOf(t).Union(b.SubjectsOf(t), StringComparer.Ordinal).ToList(),
            StringComparer.Ordinal);

    /// <summary>Variables d'environnement par service des fichiers compose (fusionnés dans l'ordre, comme docker compose).</summary>
    private static Dictionary<string, Dictionary<string, string>> Compose(params string[] fichiers)
    {
        var services = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var fichier in fichiers)
        {
            string? courant = null;
            var dansServices = false;
            foreach (var ligne in File.ReadAllLines(Path.Combine(Terraform.RacineDepot(), "deploy", "local", fichier)))
            {
                if (Regex.IsMatch(ligne, @"^[a-z]"))
                {
                    dansServices = ligne.StartsWith("services:", StringComparison.Ordinal);
                    courant = null;
                    continue;
                }

                if (!dansServices)
                {
                    continue;
                }

                if (Regex.Match(ligne, @"^  (?<nom>[a-z][a-z0-9-]*):\s*$") is { Success: true } service)
                {
                    courant = service.Groups["nom"].Value;
                    services.TryAdd(courant, new Dictionary<string, string>(StringComparer.Ordinal));
                }
                else if (courant is not null && Regex.Match(ligne, @"^      (?<cle>[A-Za-z_][A-Za-z0-9_-]*): ?(?<valeur>.*)$") is { Success: true } variable)
                {
                    services[courant][variable.Groups["cle"].Value] = variable.Groups["valeur"].Value.Trim().Trim('"');
                }
            }
        }

        return services;
    }

    [GeneratedRegex("^ServiceBus__SubjectFilters__(?<topic>[a-z0-9-]+)__[0-9]+$")]
    private static partial Regex FiltreSujet();
}
