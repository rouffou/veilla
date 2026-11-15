using System.Reflection;

using NetArchTest.Rules;

using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.BuildingBlocks.Infrastructure.Security;

using Shouldly;

using Xunit;

namespace Sepp.Testing.Architecture;

/// <summary>
/// Règles de dépendance de la clean architecture (§14.5), vérifiées automatiquement pour chaque service (ARC-20).
/// Un service hérite de cette classe et fournit ses quatre assemblies.
/// </summary>
public abstract class CleanArchitectureRules
{
    private static readonly string[] TechnicalNamespaces =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Microsoft.Extensions.Hosting",
        "Npgsql",
        "Azure",
        "System.Net.Http",
    ];

    /// <summary>Nom du service, par ex. « Referentiels » (espace de noms <c>Sepp.Referentiels</c>).</summary>
    protected abstract string ServiceName { get; }

    protected abstract Assembly DomainAssembly { get; }

    protected abstract Assembly ApplicationAssembly { get; }

    protected abstract Assembly AdaptersAssembly { get; }

    protected abstract Assembly InfrastructureAssembly { get; }

    private string Root => $"Sepp.{ServiceName}";

    [Fact]
    public void Le_domaine_ne_depend_d_aucune_technologie() =>
        Check(Types.InAssembly(DomainAssembly).ShouldNot().HaveDependencyOnAny(TechnicalNamespaces));

    [Fact]
    public void Le_domaine_ne_depend_que_du_socle_de_domaine() =>
        Check(Types.InAssembly(DomainAssembly).ShouldNot().HaveDependencyOnAny(
            $"{Root}.Application", $"{Root}.Adapters", $"{Root}.Infrastructure",
            "Sepp.BuildingBlocks.Application", "Sepp.BuildingBlocks.Infrastructure", "Sepp.BuildingBlocks.Web", "Sepp.Contracts"));

    [Fact]
    public void L_application_ne_depend_ni_des_adaptateurs_ni_de_l_infrastructure() =>
        Check(Types.InAssembly(ApplicationAssembly).ShouldNot().HaveDependencyOnAny(
            [$"{Root}.Adapters", $"{Root}.Infrastructure", "Sepp.BuildingBlocks.Infrastructure", "Sepp.BuildingBlocks.Web", .. TechnicalNamespaces]));

    [Fact]
    public void Les_adaptateurs_ne_dependent_pas_de_la_racine_de_composition() =>
        Check(Types.InAssembly(AdaptersAssembly).ShouldNot().HaveDependencyOn($"{Root}.Infrastructure"));

    [Fact]
    public void Aucun_service_ne_depend_d_un_autre_service()
    {
        var autorises = new[] { Root, "Sepp.BuildingBlocks", "Sepp.Contracts" };
        foreach (var assembly in new[] { DomainAssembly, ApplicationAssembly, AdaptersAssembly, InfrastructureAssembly })
        {
            var autresServices = assembly.GetReferencedAssemblies()
                .Select(a => a.Name!)
                .Where(n => n.StartsWith("Sepp.", StringComparison.Ordinal) && !autorises.Any(p => n.StartsWith(p, StringComparison.Ordinal)))
                .ToList();
            autresServices.ShouldBeEmpty($"{assembly.GetName().Name} référence un autre service (ARC-02, ADR 0002).");
        }
    }

    [Fact]
    public void Les_entites_du_domaine_n_exposent_pas_de_setter_public()
    {
        var fautifs = DomainAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(object)) && t.Namespace?.StartsWith(Root, StringComparison.Ordinal) == true)
            .Where(t => t.BaseType?.Name is "AggregateRoot" or "Entity")
            .SelectMany(t => t.GetProperties().Where(p => p.SetMethod?.IsPublic == true).Select(p => $"{t.Name}.{p.Name}"))
            .ToList();
        fautifs.ShouldBeEmpty("Les agrégats se modifient par leurs méthodes métier.");
    }

    [Fact]
    public void Les_cas_d_usage_sont_scelles() =>
        Check(Types.InAssembly(ApplicationAssembly).That().HaveNameEndingWith("Handler").Should().BeSealed());

    /// <summary>
    /// #442 / #472 : le modèle EF Core est mis en cache par processus et sa clé (<see cref="ModelCacheKeyFactoryChiffre"/>) intègre
    /// le chiffreur des <see cref="IContexteChiffre"/>. Un contexte qui reçoit un <see cref="FieldEncryptor"/> ou un
    /// <c>ChiffrementZoneMedicale</c> sans l'implémenter réintroduirait sans bruit la fuite de clés entre hôtes d'un même processus.
    /// </summary>
    [Fact]
    public void Tout_contexte_qui_recoit_un_chiffreur_implemente_IContexteChiffre()
    {
        var fautifs = new[] { DomainAssembly, ApplicationAssembly, AdaptersAssembly, InfrastructureAssembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && typeof(SeppDbContext).IsAssignableFrom(t))
            .Where(t => t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(c => c.GetParameters())
                .Any(p => p.ParameterType == typeof(FieldEncryptor) || p.ParameterType.Name == "ChiffrementZoneMedicale"))
            .Where(t => !typeof(IContexteChiffre).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .ToList();
        fautifs.ShouldBeEmpty("Ces contextes reçoivent un chiffreur sans implémenter IContexteChiffre (clé de cache du modèle EF, #442).");
    }

    private static void Check(ConditionList conditions)
    {
        var result = conditions.GetResult();
        result.IsSuccessful.ShouldBeTrue($"Types en infraction : {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
