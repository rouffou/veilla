using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.Referentiels.Domain.Calendrier;
using Sepp.Referentiels.Domain.Nomenclatures;
using Sepp.Referentiels.Domain.Parametres;

namespace Sepp.Referentiels.Adapters.Persistence;

/// <summary>Base de données propre au service Référentiels (ARC-02).</summary>
public sealed class ReferentielsDbContext(DbContextOptions<ReferentielsDbContext> options, ICurrentUser currentUser, TimeProvider timeProvider)
    : SeppDbContext(options, currentUser, timeProvider)
{
    public DbSet<ParametreLegal> ParametresLegaux => Set<ParametreLegal>();

    public DbSet<Nomenclature> Nomenclatures => Set<Nomenclature>();

    public DbSet<CalendrierAnnuel> Calendriers => Set<CalendrierAnnuel>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ParametreLegal>(b =>
        {
            b.ToTable("parametre_legal");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).ValueGeneratedNever();
            b.Property(p => p.Code).HasConversion(c => c.Value, v => new CodeParametre(v)).HasMaxLength(100);
            b.HasIndex(p => p.Code).IsUnique();
            Libelle(b.ComplexProperty(p => p.Libelle), "libelle");
            b.Property(p => p.Unite).HasConversion<string>().HasMaxLength(30);
            b.Property(p => p.BaseLegale).HasMaxLength(500);
            b.HasMany(p => p.Valeurs).WithOne().HasForeignKey("parametre_legal_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(p => p.Valeurs).AutoInclude();
        });

        modelBuilder.Entity<ValeurParametre>(b =>
        {
            b.ToTable("valeur_parametre");
            b.HasKey(v => v.Id);
            b.Property(v => v.Id).ValueGeneratedNever();
            b.Property(v => v.Valeur).HasPrecision(18, 4);
            Validite(b.ComplexProperty(v => v.Validite));
        });

        modelBuilder.Entity<Nomenclature>(b =>
        {
            b.ToTable("nomenclature");
            b.HasKey(n => n.Id);
            b.Property(n => n.Id).ValueGeneratedNever();
            b.Property(n => n.Code).HasMaxLength(50);
            b.HasIndex(n => n.Code).IsUnique();
            Libelle(b.ComplexProperty(n => n.Libelle), "libelle");
            b.HasMany(n => n.Entrees).WithOne().HasForeignKey("nomenclature_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(n => n.Entrees).AutoInclude();
        });

        modelBuilder.Entity<EntreeNomenclature>(b =>
        {
            b.ToTable("entree_nomenclature");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Code).HasMaxLength(50);
            b.Property(e => e.CodeParent).HasMaxLength(50);
            Libelle(b.ComplexProperty(e => e.Libelle), "libelle");
            Validite(b.ComplexProperty(e => e.Validite));
            b.HasIndex("nomenclature_id", nameof(EntreeNomenclature.Code));
        });

        modelBuilder.Entity<CalendrierAnnuel>(b =>
        {
            b.ToTable("calendrier_annuel");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).ValueGeneratedNever();
            b.HasIndex(c => c.Annee).IsUnique();
            b.HasMany(c => c.JoursSupplementaires).WithOne().HasForeignKey("calendrier_annuel_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(c => c.JoursSupplementaires).AutoInclude();
        });

        modelBuilder.Entity<JourFerieSupplementaire>(b =>
        {
            b.ToTable("jour_ferie_supplementaire");
            b.HasKey(j => j.Id);
            b.Property(j => j.Id).ValueGeneratedNever();
            b.Property(j => j.Code).HasMaxLength(50);
            Libelle(b.ComplexProperty(j => j.Libelle), "libelle");
        });
    }

    /// <summary>Libellés multilingues (DAT-07) : colonnes libelle_fr, libelle_nl, libelle_de, libelle_en.</summary>
    private static void Libelle(ComplexPropertyBuilder<LocalizedLabel> b, string prefix)
    {
        b.Property(l => l.Fr).HasColumnName($"{prefix}_fr").HasMaxLength(500);
        b.Property(l => l.Nl).HasColumnName($"{prefix}_nl").HasMaxLength(500);
        b.Property(l => l.De).HasColumnName($"{prefix}_de").HasMaxLength(500);
        b.Property(l => l.En).HasColumnName($"{prefix}_en").HasMaxLength(500);
    }

    /// <summary>Période de validité (DAT-04) : colonnes valid_from, valid_to.</summary>
    private static void Validite(ComplexPropertyBuilder<Validity> b)
    {
        b.Property(v => v.ValidFrom).HasColumnName("valid_from");
        b.Property(v => v.ValidTo).HasColumnName("valid_to");
        b.Ignore(v => v.IsOpen);
    }
}
