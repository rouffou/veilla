using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.Integrations.Domain.Bce;
using Sepp.Integrations.Domain.Correspondances;
using Sepp.Integrations.Domain.Flux;

namespace Sepp.Integrations.Adapters.Persistence;

/// <summary>
/// Base de données propre au service Intégrations (ARC-02) : journal des flux (INT-02), positions de lecture,
/// correspondances d'identifiants (§15.3) et dernières données BCE. La charge utile des échanges, qui peut contenir
/// un NISS, est chiffrée champ par champ (AES-256-GCM, ARC-45) ; aucune autre colonne ne contient de donnée personnelle.
/// </summary>
/// <remarks>
/// Le convertisseur de chiffrement est construit avec le <see cref="FieldEncryptor"/> du premier contexte (modèle EF
/// mis en cache par processus) : les clés proviennent de la configuration, identique pour tout le processus.
/// </remarks>
public sealed class IntegrationsDbContext(
    DbContextOptions<IntegrationsDbContext> options,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    FieldEncryptor encryptor)
    : SeppDbContext(options, currentUser, timeProvider)
{
    public DbSet<EchangeFlux> Journal => Set<EchangeFlux>();

    public DbSet<PositionFlux> Positions => Set<PositionFlux>();

    public DbSet<CorrespondanceIdentifiant> Correspondances => Set<CorrespondanceIdentifiant>();

    public DbSet<EntrepriseBce> Entreprises => Set<EntrepriseBce>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EchangeFlux>(b =>
        {
            b.ToTable("journal_flux");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Flux).HasConversion<string>().HasMaxLength(30);
            b.Property(e => e.Sens).HasConversion<string>().HasMaxLength(10);
            b.Property(e => e.Statut).HasConversion<string>().HasMaxLength(20);
            b.Property(e => e.TypeMessage).HasMaxLength(50);
            b.Property(e => e.CleIdempotence).HasMaxLength(200);
            b.Property(e => e.ReferenceExterne).HasMaxLength(100);
            b.Property(e => e.CodeErreur).HasMaxLength(100);
            b.Property(e => e.MessageErreur).HasMaxLength(EchangeFlux.LongueurMaximaleMessage);

            // ARC-45 : la charge utile n'est jamais stockée en clair (elle peut contenir un NISS, DAT-06).
            b.Property(e => e.ChargeUtile)
                .HasColumnName("charge_utile_chiffree")
                .HasConversion((ValueConverter)new EncryptedStringConverter(encryptor));
            b.Ignore(e => e.ChargeUtileDisponible);
            b.Ignore(e => e.EstATraiter);

            // Idempotence de la réception : une clé par flux.
            b.HasIndex(e => new { e.Flux, e.CleIdempotence }).IsUnique();
            b.HasIndex(e => new { e.Flux, e.Statut, e.RecuLe });
            b.HasIndex(e => e.RecuLe);
        });

        modelBuilder.Entity<PositionFlux>(b =>
        {
            b.ToTable("position_flux");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).ValueGeneratedNever();
            b.Property(p => p.Flux).HasConversion<string>().HasMaxLength(30);
            b.Property(p => p.Position).HasMaxLength(200);
            b.HasIndex(p => p.Flux).IsUnique();
        });

        modelBuilder.Entity<CorrespondanceIdentifiant>(b =>
        {
            b.ToTable("correspondance_identifiant");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).ValueGeneratedNever();
            b.Property(c => c.TypeExterne).HasConversion<string>().HasMaxLength(30);
            b.Property(c => c.ValeurExterne).HasMaxLength(100);
            b.Property(c => c.TypeInterne).HasConversion<string>().HasMaxLength(30);
            b.HasIndex(c => new { c.TypeExterne, c.ValeurExterne }).IsUnique();
            b.HasIndex(c => new { c.TypeInterne, c.IdentifiantInterne });
        });

        modelBuilder.Entity<EntrepriseBce>(b =>
        {
            b.ToTable("entreprise_bce");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.NumeroBce).HasMaxLength(10);
            b.HasIndex(e => e.NumeroBce).IsUnique();
            b.Property(e => e.Denomination).HasMaxLength(200);
            b.Property(e => e.FormeJuridique).HasMaxLength(50);
            b.Property(e => e.CodeNace).HasMaxLength(10);
            b.HasMany(e => e.UnitesEtablissement).WithOne().HasForeignKey("entreprise_bce_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(e => e.UnitesEtablissement).AutoInclude();
        });

        modelBuilder.Entity<UniteEtablissementBce>(b =>
        {
            b.ToTable("unite_etablissement_bce");
            b.HasKey(u => u.Id);
            b.Property(u => u.Id).ValueGeneratedNever();
            b.Property(u => u.Numero).HasMaxLength(10);
            b.Property(u => u.Denomination).HasMaxLength(200);
            b.ComplexProperty(u => u.Adresse, a =>
            {
                a.Property(x => x.Rue).HasColumnName("adresse_rue").HasMaxLength(200);
                a.Property(x => x.Numero).HasColumnName("adresse_numero").HasMaxLength(20);
                a.Property(x => x.Boite).HasColumnName("adresse_boite").HasMaxLength(20);
                a.Property(x => x.CodePostal).HasColumnName("adresse_code_postal").HasMaxLength(20);
                a.Property(x => x.Localite).HasColumnName("adresse_localite").HasMaxLength(100);
                a.Property(x => x.CodePays).HasColumnName("adresse_code_pays").HasMaxLength(2);
            });
        });
    }
}
