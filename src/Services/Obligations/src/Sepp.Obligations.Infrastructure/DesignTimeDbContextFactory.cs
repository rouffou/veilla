using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.Obligations.Adapters.Persistence;

namespace Sepp.Obligations.Infrastructure;

/// <summary>Utilisée uniquement par <c>dotnet ef</c> pour générer les migrations versionnées (DAT-09).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ObligationsDbContext>
{
    public ObligationsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ObligationsDbContext>()
            .UseNpgsql("Host=localhost;Database=obligations", o => o.MigrationsAssembly("Sepp.Obligations.Adapters"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ObligationsDbContext(options, new DesignTimeUser(), TimeProvider.System);
    }

    private sealed class DesignTimeUser : ICurrentUser
    {
        public bool IsAuthenticated => false;

        public string UserId => "design-time";

        public IReadOnlySet<string> Roles => new HashSet<string>();

        public bool HasPermission(string permission) => false;
    }
}
