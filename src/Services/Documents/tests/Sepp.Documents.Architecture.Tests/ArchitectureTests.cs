using System.Reflection;

using Sepp.Documents.Adapters.Persistence;
using Sepp.Documents.Application;
using Sepp.Documents.Domain.Modeles;
using Sepp.Documents.Infrastructure;
using Sepp.Testing.Architecture;

namespace Sepp.Documents.Architecture.Tests;

public sealed class ArchitectureTests : CleanArchitectureRules
{
    protected override string ServiceName => "Documents";

    protected override Assembly DomainAssembly => typeof(Modele).Assembly;

    protected override Assembly ApplicationAssembly => typeof(IModeleRepository).Assembly;

    protected override Assembly AdaptersAssembly => typeof(DocumentsDbContext).Assembly;

    protected override Assembly InfrastructureAssembly => typeof(DesignTimeDbContextFactory).Assembly;
}
