using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.Communications.Adapters;
using Sepp.Communications.Application;
using Sepp.Testing.Architecture;

namespace Sepp.Communications.Architecture.Tests;

/// <summary>Garde-fou : les rubriques souscrites par le code figurent dans <c>subscribes_to</c> de <c>infra/variables.tf</c> (ARC-05).</summary>
public sealed class AbonnementsTerraformTests : AbonnementsTerraformRules
{
    protected override string Service => "communications";

    protected override string ConnectionStringName => Sepp.Communications.Adapters.DependencyInjection.ConnectionStringName;

    protected override IServiceCollection Composer(IConfiguration configuration) =>
        new ServiceCollection()
            .AddCommunicationsApplication()
            .AddCommunicationsAdapters(configuration);
}
