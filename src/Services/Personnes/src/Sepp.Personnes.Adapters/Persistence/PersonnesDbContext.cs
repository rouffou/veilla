using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.Personnes.Domain.Personnes;

namespace Sepp.Personnes.Adapters.Persistence;

/// <summary>
/// Base de données propre au service Personnes (ARC-02). Le NISS et le type d'état particulier sont chiffrés
/// champ par champ (AES-256-GCM, ARC-45) ; la recherche se fait par l'index aveugle <c>niss_hash</c> (DAT-06).
/// </summary>
/// <remarks>
/// Les convertisseurs de chiffrement sont construits avec le <see cref="FieldEncryptor"/> du premier contexte
/// (modèle EF mis en cache par processus) : les clés proviennent de la configuration, identique pour tout le processus.
/// </remarks>
public sealed class PersonnesDbContext(
    DbContextOptions<PersonnesDbContext> options,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    FieldEncryptor encryptor)
    : SeppDbContext(options, currentUser, timeProvider)
{
    public DbSet<Personne> Personnes => Set<Personne>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Personne>(b =>
        {
            b.ToTable("personne");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).ValueGeneratedNever();
            b.Property(p => p.Niss)
                .HasColumnName("niss_chiffre")
                .HasConversion(new ValueConverter<Niss, string>(n => encryptor.Encrypt(n.Valeur), v => Niss.Parse(encryptor.Decrypt(v))))
                .HasMaxLength(200)
                .IsRequired();
            b.Property(p => p.NissHash).HasMaxLength(64).IsRequired();
            b.HasIndex(p => p.NissHash).IsUnique();
            b.Property(p => p.Nom).HasMaxLength(100);
            b.Property(p => p.Prenom).HasMaxLength(100);
            b.Property(p => p.Sexe).HasConversion<string>().HasMaxLength(20);
            b.Property(p => p.Langue).HasConversion<string>().HasMaxLength(2);
            b.Property(p => p.Email).HasMaxLength(254);
            b.Property(p => p.Telephone).HasMaxLength(30);
            b.Property(p => p.CanalPrefere).HasConversion<string>().HasMaxLength(20);
            b.ComplexProperty(p => p.Adresse, a =>
            {
                a.Property(x => x.Rue).HasColumnName("adresse_rue").HasMaxLength(200);
                a.Property(x => x.Numero).HasColumnName("adresse_numero").HasMaxLength(20);
                a.Property(x => x.Boite).HasColumnName("adresse_boite").HasMaxLength(20);
                a.Property(x => x.CodePostal).HasColumnName("adresse_code_postal").HasMaxLength(20);
                a.Property(x => x.Localite).HasColumnName("adresse_localite").HasMaxLength(100);
                a.Property(x => x.Pays).HasColumnName("adresse_pays").HasMaxLength(2);
            });
            b.HasMany(p => p.Occupations).WithOne().HasForeignKey("personne_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(p => p.Occupations).AutoInclude();
            b.HasMany(p => p.EtatsParticuliers).WithOne().HasForeignKey("personne_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(p => p.EtatsParticuliers).AutoInclude();
            b.HasMany(p => p.Mutations).WithOne().HasForeignKey("personne_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(p => p.Mutations).AutoInclude();
            b.Ignore(p => p.Affectations);
        });

        modelBuilder.Entity<MutationRegistreNational>(b =>
        {
            b.ToTable("mutation_registre_national");
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).ValueGeneratedNever();
            b.Property(m => m.Type).HasConversion<string>().HasMaxLength(30);

            // AFF-22 : ordre de réception, qui départage deux mutations de même type et de même date d'effet.
            b.Property(m => m.Rang);

            // Idempotence : la référence de la mutation est unique pour tout le service.
            b.Property(m => m.Reference).HasMaxLength(100).IsRequired();
            b.HasIndex(m => m.Reference).IsUnique();

            // ARC-45 : nom, prénom, adresse et date de décès (avant / après) ne sont jamais stockés en clair.
            b.Property(m => m.Avant).HasColumnName("avant_chiffre").HasMaxLength(2000)
                .HasConversion(new ValueConverter<string?, string?>(v => v == null ? null : encryptor.Encrypt(v), v => v == null ? null : encryptor.Decrypt(v)));
            b.Property(m => m.Apres).HasColumnName("apres_chiffre").HasMaxLength(2000)
                .HasConversion(new ValueConverter<string?, string?>(v => v == null ? null : encryptor.Encrypt(v), v => v == null ? null : encryptor.Decrypt(v)));
        });

        modelBuilder.Entity<Occupation>(b =>
        {
            b.ToTable("occupation");
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).ValueGeneratedNever();
            b.Property(o => o.TypeTravailleur).HasConversion<string>().HasMaxLength(30);
            b.Property(o => o.TypeContrat).HasConversion<string>().HasMaxLength(30);
            b.Property(o => o.ReferenceDimona).HasMaxLength(50);

            // AFF-20 : la référence DIMONA est la clé d'idempotence de l'alimentation automatique.
            b.HasIndex(o => o.ReferenceDimona).IsUnique().HasFilter("reference_dimona IS NOT NULL");
            b.HasIndex(o => o.AffilieId);
            b.HasIndex(o => o.AffilieUtilisateurId);
            b.HasMany(o => o.Affectations).WithOne().HasForeignKey("occupation_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(o => o.Affectations).AutoInclude();
        });

        modelBuilder.Entity<Affectation>(b =>
        {
            b.ToTable("affectation");
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).ValueGeneratedNever();
            b.HasIndex(a => a.PosteId);

            // DAT-04 : période de validité [valid_from, valid_to[.
            b.ComplexProperty(a => a.Validite, v =>
            {
                v.Property(x => x.ValidFrom).HasColumnName("valid_from");
                v.Property(x => x.ValidTo).HasColumnName("valid_to");
                v.Ignore(x => x.IsOpen);
            });
        });

        modelBuilder.Entity<EtatParticulier>(b =>
        {
            b.ToTable("etat_particulier");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();

            // ARC-45 : « grossesse » ou « allaitement » n'est jamais stocké en clair.
            b.Property(e => e.Type)
                .HasConversion(new ValueConverter<TypeEtatParticulier, string>(
                    t => encryptor.Encrypt(t.ToString()),
                    v => Enum.Parse<TypeEtatParticulier>(encryptor.Decrypt(v))))
                .HasColumnName("type_chiffre")
                .HasMaxLength(200);
        });
    }
}
