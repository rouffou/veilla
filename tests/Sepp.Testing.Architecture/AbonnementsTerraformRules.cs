using System.Text.RegularExpressions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Infrastructure.Messaging;

using Shouldly;

using Xunit;

namespace Sepp.Testing.Architecture;

/// <summary>
/// Garde-fou contre la dérive des abonnements (ARC-05, ARC-33) : les rubriques auxquelles un service s'abonne par
/// <c>AddIntegrationEventHandler</c> doivent figurer dans son <c>subscribes_to</c> de <c>infra/variables.tf</c>, faute de
/// quoi le message n'atteindrait jamais le service une fois déployé. Un service hérite de cette classe et fournit son
/// nom Terraform et sa composition.
/// </summary>
public abstract class AbonnementsTerraformRules
{
    /// <summary>Nom du service dans le catalogue Terraform, par ex. « postes-risques ».</summary>
    protected abstract string Service { get; }

    /// <summary>Nom de la chaîne de connexion du service (aucune connexion n'est ouverte).</summary>
    protected abstract string ConnectionStringName { get; }

    /// <summary>
    /// Compose les services du service tels que le fait sa racine de composition (sans ouvrir de connexion), avec la
    /// configuration de développement copiée depuis le projet d'infrastructure.
    /// </summary>
    protected abstract IServiceCollection Composer(IConfiguration configuration);

    [Fact]
    public void Le_service_est_declare_dans_le_catalogue_terraform() =>
        VariablesTerraform.Lire().Services.ContainsKey(Service)
            .ShouldBeTrue($"Le service '{Service}' est absent de var.services (infra/variables.tf).");

    [Fact]
    public void Les_rubriques_souscrites_par_le_code_figurent_dans_subscribes_to()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"ConnectionStrings:{ConnectionStringName}"] = "Host=localhost;Database=abonnements-terraform" })
            .Build();
        var souscrites = RubriquesSouscrites(Composer(configuration));
        souscrites.ShouldNotBeEmpty($"Aucun gestionnaire d'événement enregistré pour '{Service}' : le service n'a pas à porter ce test.");

        var declarees = VariablesTerraform.Lire().Services[Service].SubscribesTo;
        var manquantes = souscrites.Except(declarees, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        manquantes.ShouldBeEmpty(
            $"'{Service}' s'abonne par le code à des rubriques absentes de subscribes_to (infra/variables.tf) : {string.Join(", ", manquantes)}.");
    }

    /// <summary>Rubriques des gestionnaires enregistrés (registre <see cref="IntegrationEventSubscriptions"/>).</summary>
    public static IReadOnlyCollection<string> RubriquesSouscrites(IServiceCollection services)
    {
        var abonnements = services
            .Where(d => d.ServiceType == typeof(IntegrationEventSubscriptions))
            .Select(d => d.ImplementationInstance)
            .OfType<IntegrationEventSubscriptions>()
            .SingleOrDefault();
        return abonnements is null ? [] : [.. abonnements.Topics];
    }
}

/// <summary>Contenu utile de <c>infra/variables.tf</c> : services, topics souscrits et filtres de sujets.</summary>
public sealed record VariablesTerraform(IReadOnlyDictionary<string, ServiceTerraform> Services)
{
    /// <summary>Analyse de <c>infra/variables.tf</c> (chemin relatif à la racine du dépôt, trouvée en remontant depuis le répertoire de test).</summary>
    public static VariablesTerraform Lire() => Analyser(File.ReadAllText(Path.Combine(RacineDepot(), "infra", "variables.tf")));

    public static string RacineDepot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "infra", "variables.tf")) && File.Exists(Path.Combine(dir.FullName, "Sepp.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Racine du dépôt introuvable (infra/variables.tf, Sepp.slnx).");
    }

    /// <summary>Extrait le catalogue <c>default = { ... }</c> de <c>variable "services"</c> ; les commentaires sont ignorés.</summary>
    public static VariablesTerraform Analyser(string contenu)
    {
        var texte = Regex.Replace(contenu, @"#[^\r\n]*", string.Empty);
        var debutVariable = texte.IndexOf("variable \"services\"", StringComparison.Ordinal);
        debutVariable.ShouldBeGreaterThanOrEqualTo(0, "variable \"services\" introuvable.");
        var marqueur = Regex.Match(texte[debutVariable..], @"default\s*=\s*\{");
        marqueur.Success.ShouldBeTrue("default = { ... } de variable \"services\" introuvable.");
        var ouvrante = debutVariable + marqueur.Index + marqueur.Length - 1;
        var catalogue = texte[(ouvrante + 1)..FinAccolade(texte, ouvrante)];

        var services = new Dictionary<string, ServiceTerraform>(StringComparer.Ordinal);
        foreach (Match entree in Regex.Matches(catalogue, "\"(?<nom>[a-z][a-z0-9-]*)\"\\s*=\\s*\\{"))
        {
            var ouvre = entree.Index + entree.Length - 1;
            // Les accolades imbriquées (subject_filters) appartiennent au bloc du service courant.
            var bloc = catalogue[ouvre..(FinAccolade(catalogue, ouvre) + 1)];
            if (services.ContainsKey(entree.Groups["nom"].Value) || !bloc.Contains("zone", StringComparison.Ordinal))
            {
                continue;
            }

            services[entree.Groups["nom"].Value] = new ServiceTerraform(
                Liste(bloc, "subscribes_to"),
                FiltresSujets(bloc));
        }

        return new VariablesTerraform(services);
    }

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

    private static List<string> Liste(string bloc, string attribut)
    {
        var m = Regex.Match(bloc, attribut + @"\s*=\s*\[(?<valeurs>[^\]]*)\]");
        return m.Success ? [.. Regex.Matches(m.Groups["valeurs"].Value, "\"([^\"]+)\"").Select(v => v.Groups[1].Value)] : [];
    }

    private static Dictionary<string, IReadOnlyList<string>> FiltresSujets(string bloc)
    {
        var m = Regex.Match(bloc, @"subject_filters\s*=\s*\{");
        if (!m.Success)
        {
            return new Dictionary<string, IReadOnlyList<string>>();
        }

        var ouvre = m.Index + m.Length - 1;
        var interieur = bloc[(ouvre + 1)..FinAccolade(bloc, ouvre)];
        return Regex.Matches(interieur, @"(?<topic>[a-z][a-z0-9-]*)\s*=\s*\[(?<valeurs>[^\]]*)\]")
            .ToDictionary(
                e => e.Groups["topic"].Value,
                e => (IReadOnlyList<string>)[.. Regex.Matches(e.Groups["valeurs"].Value, "\"([^\"]+)\"").Select(v => v.Groups[1].Value)]);
    }
}

/// <summary>Service du catalogue Terraform : rubriques souscrites et sujets acceptés par rubrique.</summary>
public sealed record ServiceTerraform(IReadOnlyList<string> SubscribesTo, IReadOnlyDictionary<string, IReadOnlyList<string>> SubjectFilters);
