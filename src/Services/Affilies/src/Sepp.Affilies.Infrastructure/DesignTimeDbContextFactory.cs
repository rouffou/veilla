using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

using Sepp.Affilies.Adapters.Persistence;
using Sepp.BuildingBlocks.Application.Security;

namespace Sepp.Affilies.Infrastructure;

/// <summary>Utilisée uniquement par <c>dotnet ef</c> pour générer les migrations versionnées (DAT-09).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AffiliesDbContext>
{
    public AffiliesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AffiliesDbContext>()
            .UseNpgsql("Host=localhost;Database=affilies", o => o.MigrationsAssembly("Sepp.Affilies.Adapters"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AffiliesDbContext(options, new DesignTimeUser(), TimeProvider.System);
    }

    private sealed class DesignTimeUser : ICurrentUser
    {
        public bool IsAuthenticated => false;

        public string UserId => "design-time";

        public IReadOnlySet<string> Roles => new HashSet<string>();

        public bool HasPermission(string permission) => false;
    }
}
