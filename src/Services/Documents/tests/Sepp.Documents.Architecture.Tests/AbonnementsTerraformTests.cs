using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.Documents.Adapters;
using Sepp.Documents.Application;
using Sepp.Testing.Architecture;

namespace Sepp.Documents.Architecture.Tests;

/// <summary>Garde-fou : les rubriques souscrites par le code figurent dans <c>subscribes_to</c> de <c>infra/variables.tf</c> (ARC-05).</summary>
public sealed class AbonnementsTerraformTests : AbonnementsTerraformRules
{
    protected override string Service => "documents";

    protected override string ConnectionStringName => Sepp.Documents.Adapters.DependencyInjection.ConnectionStringName;

    protected override IServiceCollection Composer(IConfiguration configuration) =>
        new ServiceCollection()
            .AddDocumentsApplication()
            .AddDocumentsAdapters(configuration);
}
