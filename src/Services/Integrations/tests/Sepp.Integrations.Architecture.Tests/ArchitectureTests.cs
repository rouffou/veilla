using System.Reflection;

using Sepp.Integrations.Adapters.Persistence;
using Sepp.Integrations.Application;
using Sepp.Integrations.Domain.Flux;
using Sepp.Integrations.Infrastructure;
using Sepp.Testing.Architecture;

namespace Sepp.Integrations.Architecture.Tests;

public sealed class ArchitectureTests : CleanArchitectureRules
{
    protected override string ServiceName => "Integrations";

    protected override Assembly DomainAssembly => typeof(EchangeFlux).Assembly;

    protected override Assembly ApplicationAssembly => typeof(IJournalFluxRepository).Assembly;

    protected override Assembly AdaptersAssembly => typeof(IntegrationsDbContext).Assembly;

    protected override Assembly InfrastructureAssembly => typeof(DesignTimeDbContextFactory).Assembly;
}
