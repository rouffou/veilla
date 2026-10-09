using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.Audit.Adapters;
using Sepp.Audit.Application;
using Sepp.Testing.Architecture;

namespace Sepp.Audit.Architecture.Tests;

/// <summary>Garde-fou : les rubriques souscrites par le code figurent dans <c>subscribes_to</c> de <c>infra/variables.tf</c> (ARC-05).</summary>
public sealed class AbonnementsTerraformTests : AbonnementsTerraformRules
{
    protected override string Service => "audit";

    protected override string ConnectionStringName => Sepp.Audit.Adapters.DependencyInjection.ConnectionStringName;

    protected override IServiceCollection Composer(IConfiguration configuration) =>
        new ServiceCollection()
            .AddAuditApplication()
            .AddAuditAdapters(configuration);
}
