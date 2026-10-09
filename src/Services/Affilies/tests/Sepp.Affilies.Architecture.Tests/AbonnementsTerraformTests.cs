using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.Affilies.Adapters;
using Sepp.Affilies.Application;
using Sepp.Testing.Architecture;

namespace Sepp.Affilies.Architecture.Tests;

/// <summary>Garde-fou : les rubriques souscrites par le code figurent dans <c>subscribes_to</c> de <c>infra/variables.tf</c> (ARC-05).</summary>
public sealed class AbonnementsTerraformTests : AbonnementsTerraformRules
{
    protected override string Service => "affilies";

    protected override string ConnectionStringName => Sepp.Affilies.Adapters.DependencyInjection.ConnectionStringName;

    protected override IServiceCollection Composer(IConfiguration configuration) =>
        new ServiceCollection()
            .AddAffiliesApplication()
            .AddAffiliesAdapters(configuration);
}
