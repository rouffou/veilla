using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.Planification.Adapters;
using Sepp.Planification.Application;
using Sepp.Testing.Architecture;

namespace Sepp.Planification.Architecture.Tests;

/// <summary>Garde-fou : les rubriques souscrites par le code figurent dans <c>subscribes_to</c> de <c>infra/variables.tf</c> (ARC-05).</summary>
public sealed class AbonnementsTerraformTests : AbonnementsTerraformRules
{
    protected override string Service => "planification";

    protected override string ConnectionStringName => Sepp.Planification.Adapters.DependencyInjection.ConnectionStringName;

    protected override IServiceCollection Composer(IConfiguration configuration) =>
        new ServiceCollection()
            .AddPlanificationApplication()
            .AddPlanificationAdapters(configuration);
}
