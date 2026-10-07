using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.Planification.Adapters.Persistence;

namespace Sepp.Planification.Infrastructure;

/// <summary>Utilisée uniquement par <c>dotnet ef</c> pour générer les migrations versionnées (DAT-09).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PlanificationDbContext>
{
    public PlanificationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PlanificationDbContext>()
            .UseNpgsql("Host=localhost;Database=planification", o => o.MigrationsAssembly("Sepp.Planification.Adapters"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new PlanificationDbContext(options, new DesignTimeUser(), TimeProvider.System);
    }

    private sealed class DesignTimeUser : ICurrentUser
    {
        public bool IsAuthenticated => false;

        public string UserId => "design-time";

        public IReadOnlySet<string> Roles => new HashSet<string>();

        public bool HasPermission(string permission) => false;
    }
}
