using System.Reflection;

using Sepp.Affilies.Adapters.Persistence;
using Sepp.Affilies.Application;
using Sepp.Affilies.Domain.Affilies;
using Sepp.Affilies.Infrastructure;
using Sepp.Testing.Architecture;

namespace Sepp.Affilies.Architecture.Tests;

public sealed class ArchitectureTests : CleanArchitectureRules
{
    protected override string ServiceName => "Affilies";

    protected override Assembly DomainAssembly => typeof(Affilie).Assembly;

    protected override Assembly ApplicationAssembly => typeof(IAffilieRepository).Assembly;

    protected override Assembly AdaptersAssembly => typeof(AffiliesDbContext).Assembly;

    protected override Assembly InfrastructureAssembly => typeof(DesignTimeDbContextFactory).Assembly;
}
