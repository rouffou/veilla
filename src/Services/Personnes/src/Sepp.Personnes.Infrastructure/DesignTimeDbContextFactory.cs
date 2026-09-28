using System.Security.Cryptography;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.Personnes.Adapters.Persistence;

namespace Sepp.Personnes.Infrastructure;

/// <summary>Utilisée uniquement par <c>dotnet ef</c> pour générer les migrations versionnées (DAT-09).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PersonnesDbContext>
{
    public PersonnesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PersonnesDbContext>()
            .UseNpgsql("Host=localhost;Database=personnes", o => o.MigrationsAssembly("Sepp.Personnes.Adapters"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new PersonnesDbContext(options, new DesignTimeUser(), TimeProvider.System, new FieldEncryptor(new EphemeralKey()));
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
