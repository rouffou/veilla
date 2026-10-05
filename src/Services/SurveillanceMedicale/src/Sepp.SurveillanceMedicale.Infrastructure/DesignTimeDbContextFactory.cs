using System.Security.Cryptography;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.SurveillanceMedicale.Adapters.Persistence;

namespace Sepp.SurveillanceMedicale.Infrastructure;

/// <summary>Utilisée uniquement par <c>dotnet ef</c> pour générer les migrations versionnées (DAT-09).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SurveillanceMedicaleDbContext>
{
    public SurveillanceMedicaleDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SurveillanceMedicaleDbContext>()
            .UseNpgsql("Host=localhost;Database=surveillance_medicale", o => o.MigrationsAssembly("Sepp.SurveillanceMedicale.Adapters"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new SurveillanceMedicaleDbContext(
            options, new DesignTimeUser(), TimeProvider.System, new ChiffrementZoneMedicale(new FieldEncryptor(new EphemeralKey())));
    }

    private sealed class DesignTimeUser : ICurrentUser
    {
        public bool IsAuthenticated => false;

        public string UserId => "design-time";

        public IReadOnlySet<string> Roles => new HashSet<string>();

        public bool HasPermission(string permission) => false;
    }

    /// <summary>Clé jetable : la génération du modèle ne chiffre aucune donnée.</summary>
    private sealed class EphemeralKey : IFieldKeyProvider
    {
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

        public string CurrentKeyId => "design-time";

        public byte[] GetKey(string keyId) => _key;
    }
}
