using System.Reflection;

using Sepp.Personnes.Adapters.Persistence;
using Sepp.Personnes.Application;
using Sepp.Personnes.Domain.Personnes;
using Sepp.Personnes.Infrastructure;
using Sepp.Testing.Architecture;

namespace Sepp.Personnes.Architecture.Tests;

public sealed class ArchitectureTests : CleanArchitectureRules
{
    protected override string ServiceName => "Personnes";

    protected override Assembly DomainAssembly => typeof(Personne).Assembly;

    protected override Assembly ApplicationAssembly => typeof(IPersonneRepository).Assembly;

    protected override Assembly AdaptersAssembly => typeof(PersonnesDbContext).Assembly;

    protected override Assembly InfrastructureAssembly => typeof(DesignTimeDbContextFactory).Assembly;
}
