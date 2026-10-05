using System.Reflection;

using Sepp.PostesRisques.Adapters.Persistence;
using Sepp.PostesRisques.Application;
using Sepp.PostesRisques.Domain.Postes;
using Sepp.PostesRisques.Infrastructure;
using Sepp.Testing.Architecture;

namespace Sepp.PostesRisques.Architecture.Tests;

public sealed class ArchitectureTests : CleanArchitectureRules
{
    protected override string ServiceName => "PostesRisques";

    protected override Assembly DomainAssembly => typeof(Poste).Assembly;

    protected override Assembly ApplicationAssembly => typeof(IPosteRepository).Assembly;

    protected override Assembly AdaptersAssembly => typeof(PostesRisquesDbContext).Assembly;

    protected override Assembly InfrastructureAssembly => typeof(DesignTimeDbContextFactory).Assembly;
}
