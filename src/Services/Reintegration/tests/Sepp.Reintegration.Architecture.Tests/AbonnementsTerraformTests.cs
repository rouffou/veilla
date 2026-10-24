using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.Reintegration.Adapters;
using Sepp.Reintegration.Application;
using Sepp.Testing.Architecture;

using Shouldly;

using Xunit;

namespace Sepp.Reintegration.Architecture.Tests;

/// <summary>Garde-fou : les rubriques souscrites par le code figurent dans <c>subscribes_to</c> de <c>infra/variables.tf</c> (ARC-05).</summary>
public sealed class AbonnementsTerraformTests : AbonnementsTerraformRules
{
    protected override string Service => "reintegration";

    protected override string ConnectionStringName => Sepp.Reintegration.Adapters.DependencyInjection.ConnectionStringName;

    protected override IServiceCollection Composer(IConfiguration configuration) =>
        new ServiceCollection()
            .AddReintegrationApplication()
            .AddReintegrationAdapters(configuration);

    /// <summary>Les filtres de sujets de Terraform laissent passer chaque contrat réellement consommé.</summary>
    [Fact]
    public void Les_filtres_de_sujets_laissent_passer_les_contrats_consommes()
    {
        var filtres = VariablesTerraform.Lire().Services[Service].SubjectFilters;
        string[] consommes =
        [
            "integrations.incapacite-notifiee.v1", "obligations.obligation-creee.v1", "obligations.reprise-enregistree.v1",
            "planification.absence-rendez-vous-constatee.v1", "referentiels.parametre-legal-modifie.v1", "referentiels.jours-feries-modifies.v1",
        ];
        foreach (var contrat in consommes)
        {
            var topic = contrat[..contrat.IndexOf('.', StringComparison.Ordinal)];
            if (filtres.TryGetValue(topic, out var sujets))
            {
                sujets.ShouldContain(contrat, $"{contrat} est filtré par subject_filters.{topic} (infra/variables.tf).");
            }
        }
    }
}
