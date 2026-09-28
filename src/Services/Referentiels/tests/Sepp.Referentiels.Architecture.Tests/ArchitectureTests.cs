using System.Reflection;

using Sepp.Referentiels.Adapters.Persistence;
using Sepp.Referentiels.Application;
using Sepp.Referentiels.Domain.Parametres;
using Sepp.Referentiels.Infrastructure;
using Sepp.Testing.Architecture;

namespace Sepp.Referentiels.Architecture.Tests;

public sealed class ArchitectureTests : CleanArchitectureRules
{
    protected override string ServiceName => "Referentiels";

    protected override Assembly DomainAssembly => typeof(ParametreLegal).Assembly;

    protected override Assembly ApplicationAssembly => typeof(IParametreLegalRepository).Assembly;

    protected override Assembly AdaptersAssembly => typeof(ReferentielsDbContext).Assembly;

    protected override Assembly InfrastructureAssembly => typeof(DesignTimeDbContextFactory).Assembly;
}
