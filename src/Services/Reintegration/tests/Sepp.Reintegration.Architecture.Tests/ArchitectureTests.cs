using System.Reflection;

using Sepp.Reintegration.Adapters.Persistence;
using Sepp.Reintegration.Application;
using Sepp.Reintegration.Domain.Incapacites;
using Sepp.Reintegration.Infrastructure;
using Sepp.Testing.Architecture;

namespace Sepp.Reintegration.Architecture.Tests;

public sealed class ArchitectureTests : CleanArchitectureRules
{
    protected override string ServiceName => "Reintegration";

    protected override Assembly DomainAssembly => typeof(Incapacite).Assembly;

    protected override Assembly ApplicationAssembly => typeof(IIncapaciteRepository).Assembly;

    protected override Assembly AdaptersAssembly => typeof(ReintegrationDbContext).Assembly;

    protected override Assembly InfrastructureAssembly => typeof(DesignTimeDbContextFactory).Assembly;
}
