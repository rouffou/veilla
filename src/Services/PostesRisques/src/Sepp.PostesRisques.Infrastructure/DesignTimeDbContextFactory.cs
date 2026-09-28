using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.PostesRisques.Adapters.Persistence;

namespace Sepp.PostesRisques.Infrastructure;

/// <summary>Utilisée uniquement par <c>dotnet ef</c> pour générer les migrations versionnées (DAT-09).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PostesRisquesDbContext>
{
    public PostesRisquesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PostesRisquesDbContext>()
            .UseNpgsql("Host=localhost;Database=postes_risques", o => o.MigrationsAssembly("Sepp.PostesRisques.Adapters"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new PostesRisquesDbContext(options, new DesignTimeUser(), TimeProvider.System);
    }

    private sealed class DesignTimeUser : ICurrentUser
    {
        public bool IsAuthenticated => false;

        public string UserId => "design-time";

        public IReadOnlySet<string> Roles => new HashSet<string>();

        public bool HasPermission(string permission) => false;
    }
}
