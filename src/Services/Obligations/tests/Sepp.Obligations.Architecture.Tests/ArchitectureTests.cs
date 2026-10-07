using System.Reflection;

using Sepp.Obligations.Adapters.Persistence;
using Sepp.Obligations.Application;
using Sepp.Obligations.Domain.Obligations;
using Sepp.Obligations.Infrastructure;
using Sepp.Testing.Architecture;

namespace Sepp.Obligations.Architecture.Tests;

public sealed class ArchitectureTests : CleanArchitectureRules
{
    protected override string ServiceName => "Obligations";

    protected override Assembly DomainAssembly => typeof(Obligation).Assembly;

    protected override Assembly ApplicationAssembly => typeof(IObligationRepository).Assembly;

    protected override Assembly AdaptersAssembly => typeof(ObligationsDbContext).Assembly;

    protected override Assembly InfrastructureAssembly => typeof(DesignTimeDbContextFactory).Assembly;
}
