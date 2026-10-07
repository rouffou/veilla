using System.Reflection;

using Sepp.Communications.Adapters.Persistence;
using Sepp.Communications.Application;
using Sepp.Communications.Domain.Messages;
using Sepp.Communications.Infrastructure;
using Sepp.Testing.Architecture;

namespace Sepp.Communications.Architecture.Tests;

public sealed class ArchitectureTests : CleanArchitectureRules
{
    protected override string ServiceName => "Communications";

    protected override Assembly DomainAssembly => typeof(Message).Assembly;

    protected override Assembly ApplicationAssembly => typeof(IMessageRepository).Assembly;

    protected override Assembly AdaptersAssembly => typeof(CommunicationsDbContext).Assembly;

    protected override Assembly InfrastructureAssembly => typeof(DesignTimeDbContextFactory).Assembly;
}
