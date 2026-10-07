using System.Reflection;

using Sepp.Planification.Adapters.Persistence;
using Sepp.Planification.Application;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Infrastructure;
using Sepp.Testing.Architecture;

namespace Sepp.Planification.Architecture.Tests;

public sealed class ArchitectureTests : CleanArchitectureRules
{
    protected override string ServiceName => "Planification";

    protected override Assembly DomainAssembly => typeof(Creneau).Assembly;

    protected override Assembly ApplicationAssembly => typeof(ILieuRepository).Assembly;

    protected override Assembly AdaptersAssembly => typeof(PlanificationDbContext).Assembly;

    protected override Assembly InfrastructureAssembly => typeof(DesignTimeDbContextFactory).Assembly;
}
