using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

using Sepp.Audit.Adapters.Persistence;
using Sepp.BuildingBlocks.Application.Security;

namespace Sepp.Audit.Infrastructure;

/// <summary>Utilisée uniquement par <c>dotnet ef</c> pour générer les migrations versionnées (DAT-09).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql("Host=localhost;Database=audit", o => o.MigrationsAssembly("Sepp.Audit.Adapters"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AuditDbContext(options, new DesignTimeUser(), TimeProvider.System);
    }

    private sealed class DesignTimeUser : ICurrentUser
    {
        public bool IsAuthenticated => false;

        public string UserId => "design-time";

        public IReadOnlySet<string> Roles => new HashSet<string>();

        public bool HasPermission(string permission) => false;
    }
}
