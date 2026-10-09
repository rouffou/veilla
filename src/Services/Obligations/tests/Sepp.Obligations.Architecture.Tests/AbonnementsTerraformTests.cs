using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.Obligations.Adapters;
using Sepp.Obligations.Application;
using Sepp.Testing.Architecture;

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
}
