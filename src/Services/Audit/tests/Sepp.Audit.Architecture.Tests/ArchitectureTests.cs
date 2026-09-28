using System.Reflection;

using Sepp.Audit.Adapters.Persistence;
using Sepp.Audit.Application;
using Sepp.Audit.Domain.Journal;
using Sepp.Audit.Infrastructure;
using Sepp.Testing.Architecture;

namespace Sepp.Audit.Architecture.Tests;

public sealed class ArchitectureTests : CleanArchitectureRules
{
    protected override string ServiceName => "Audit";

    protected override Assembly DomainAssembly => typeof(EntreeAudit).Assembly;

    protected override Assembly ApplicationAssembly => typeof(IJournalAuditRepository).Assembly;

    protected override Assembly AdaptersAssembly => typeof(AuditDbContext).Assembly;

    protected override Assembly InfrastructureAssembly => typeof(DesignTimeDbContextFactory).Assembly;
}
