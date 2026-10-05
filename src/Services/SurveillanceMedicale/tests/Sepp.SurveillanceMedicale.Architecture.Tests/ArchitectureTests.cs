using System.Reflection;

using Sepp.SurveillanceMedicale.Adapters.Persistence;
using Sepp.SurveillanceMedicale.Application;
using Sepp.SurveillanceMedicale.Domain.Dossiers;
using Sepp.SurveillanceMedicale.Infrastructure;
using Sepp.Testing.Architecture;

namespace Sepp.SurveillanceMedicale.Architecture.Tests;

public sealed class ArchitectureTests : CleanArchitectureRules
{
    protected override string ServiceName => "SurveillanceMedicale";

    protected override Assembly DomainAssembly => typeof(DossierSante).Assembly;

    protected override Assembly ApplicationAssembly => typeof(IDossierSanteRepository).Assembly;

    protected override Assembly AdaptersAssembly => typeof(SurveillanceMedicaleDbContext).Assembly;

    protected override Assembly InfrastructureAssembly => typeof(DesignTimeDbContextFactory).Assembly;
}
