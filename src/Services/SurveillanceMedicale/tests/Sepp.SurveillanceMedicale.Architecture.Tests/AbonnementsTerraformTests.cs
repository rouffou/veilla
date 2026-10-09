using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.SurveillanceMedicale.Adapters;
using Sepp.SurveillanceMedicale.Application;
using Sepp.Testing.Architecture;

namespace Sepp.SurveillanceMedicale.Architecture.Tests;

/// <summary>Garde-fou : les rubriques souscrites par le code figurent dans <c>subscribes_to</c> de <c>infra/variables.tf</c> (ARC-05).</summary>
public sealed class AbonnementsTerraformTests : AbonnementsTerraformRules
{
    protected override string Service => "surveillance-medicale";

    protected override string ConnectionStringName => Sepp.SurveillanceMedicale.Adapters.DependencyInjection.ConnectionStringName;

    protected override IServiceCollection Composer(IConfiguration configuration) =>
        new ServiceCollection()
            .AddSurveillanceMedicaleApplication()
            .AddSurveillanceMedicaleAdapters(configuration);
}
