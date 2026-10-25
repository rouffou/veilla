using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.Obligations.Adapters;
using Sepp.Obligations.Application;
using Sepp.Testing.Architecture;

using Shouldly;

using Xunit;

namespace Sepp.Obligations.Architecture.Tests;

/// <summary>Garde-fou : les rubriques souscrites par le code figurent dans <c>subscribes_to</c> de <c>infra/variables.tf</c> (ARC-05).</summary>
public sealed class AbonnementsTerraformTests : AbonnementsTerraformRules
{
    protected override string Service => "obligations";

    protected override string ConnectionStringName => Sepp.Obligations.Adapters.DependencyInjection.ConnectionStringName;

    protected override IServiceCollection Composer(IConfiguration configuration) =>
        new ServiceCollection()
            .AddObligationsApplication()
            .AddObligationsAdapters(configuration);

    /// <summary>Les filtres de sujets de Terraform laissent passer chaque contrat d'incapacité et de trajet consommé (SAN-60, SAN-62).</summary>
    [Theory]
    [InlineData("integrations.incapacite-notifiee.v1")]
    [InlineData("reintegration.incapacite-saisie.v1")]
    [InlineData("reintegration.trajet-demarre.v1")]
    [InlineData("reintegration.trajet-termine.v1")]
    public void Les_filtres_de_sujets_laissent_passer_les_contrats_consommes(string contrat)
    {
        var topic = contrat[..contrat.IndexOf('.', StringComparison.Ordinal)];
        var service = VariablesTerraform.Lire().Services[Service];
        service.SubscribesTo.ShouldContain(topic);
        if (service.SubjectFilters.TryGetValue(topic, out var sujets))
        {
            sujets.ShouldContain(contrat, $"{contrat} est filtré par subject_filters.{topic} (infra/variables.tf).");
        }
    }
}
