using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.Referentiels.Adapters.Persistence;

namespace Sepp.Referentiels.Infrastructure;

/// <summary>Utilisée uniquement par <c>dotnet ef</c> pour générer les migrations versionnées (DAT-09).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ReferentielsDbContext>
{
    public ReferentielsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ReferentielsDbContext>()
            .UseNpgsql("Host=localhost;Database=referentiels", o => o.MigrationsAssembly("Sepp.Referentiels.Adapters"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ReferentielsDbContext(options, new DesignTimeUser(), TimeProvider.System);
    }

    private sealed class DesignTimeUser : ICurrentUser
    {
        public bool IsAuthenticated => false;

        public string UserId => "design-time";

        public IReadOnlySet<string> Roles => new HashSet<string>();

        public bool HasPermission(string permission) => false;
    }
}
