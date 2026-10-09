using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.Integrations.Adapters;
using Sepp.Integrations.Application;
using Sepp.Testing.Architecture;

namespace Sepp.Integrations.Architecture.Tests;

/// <summary>Garde-fou : les rubriques souscrites par le code figurent dans <c>subscribes_to</c> de <c>infra/variables.tf</c> (ARC-05).</summary>
public sealed class AbonnementsTerraformTests : AbonnementsTerraformRules
{
    protected override string Service => "integrations";

    protected override string ConnectionStringName => Sepp.Integrations.Adapters.DependencyInjection.ConnectionStringName;

    protected override IServiceCollection Composer(IConfiguration configuration) =>
        new ServiceCollection()
            .AddIntegrationsApplication()
            .AddIntegrationsAdapters(configuration);
}
