using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sepp.Affilies.Domain.Affilies;
using Sepp.Affilies.Domain.Ecarts;
using Sepp.Affilies.Domain.Groupes;
using Sepp.Affilies.Domain.Historique;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Infrastructure.Persistence;

namespace Sepp.Affilies.Adapters.Persistence;

/// <summary>Base de données propre au service Affiliés (ARC-02).</summary>
public sealed class AffiliesDbContext(DbContextOptions<AffiliesDbContext> options, ICurrentUser currentUser, TimeProvider timeProvider)
    : SeppDbContext(options, currentUser, timeProvider)
{
    public DbSet<Affilie> Affilies => Set<Affilie>();

    public DbSet<Groupe> Groupes => Set<Groupe>();

    public DbSet<ModificationAffilie> Historique => Set<ModificationAffilie>();

    public DbSet<EcartSynchronisation> EcartsSynchronisation => Set<EcartSynchronisation>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Groupe>(b =>
        {
            b.ToTable("groupe");
            b.HasKey(g => g.Id);
            b.Property(g => g.Id).ValueGeneratedNever();
            b.Property(g => g.Nom).HasMaxLength(200);
        });

        modelBuilder.Entity<Affilie>(b =>
        {
            b.ToTable("affilie");
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).ValueGeneratedNever();
            b.Property(a => a.NumeroBce).HasConversion(n => n.Value, v => new NumeroBce(v)).HasMaxLength(10);
            // DAT-01 : le numéro BCE est un identifiant métier unique parmi les affiliés non supprimés.
            b.HasIndex(a => a.NumeroBce).IsUnique().HasFilter("deleted_at IS NULL");
            b.Property(a => a.Denomination).HasMaxLength(200);
            b.HasIndex(a => a.Denomination);
            b.Property(a => a.FormeJuridique).HasMaxLength(50);
            b.Property(a => a.CodeNace).HasMaxLength(10);
            b.Property(a => a.CommissionParitaire).HasMaxLength(10);
            b.Property(a => a.CategorieTarifaire).HasConversion<string>().HasMaxLength(1);
            b.Property(a => a.Langue).HasConversion<string>().HasMaxLength(2);
            b.Property(a => a.RegimeLinguistique).HasConversion<string>().HasMaxLength(30);
            b.Property(a => a.Statut).HasConversion<string>().HasMaxLength(20);
            b.HasOne<Groupe>().WithMany().HasForeignKey(a => a.GroupeId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
            b.Ignore(a => a.OperationEnCours);

            b.HasMany(a => a.UnitesEtablissement).WithOne().HasForeignKey("affilie_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.HasMany(a => a.Contacts).WithOne().HasForeignKey("affilie_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.HasMany(a => a.OrganesConcertation).WithOne().HasForeignKey("affilie_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.HasMany(a => a.Operations).WithOne().HasForeignKey("affilie_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(a => a.UnitesEtablissement).AutoInclude();
            b.Navigation(a => a.Contacts).AutoInclude();
            b.Navigation(a => a.OrganesConcertation).AutoInclude();
            b.Navigation(a => a.Operations).AutoInclude();
        });

        modelBuilder.Entity<UniteEtablissement>(b =>
        {
            b.ToTable("unite_etablissement");
            b.HasKey(u => u.Id);
            b.Property(u => u.Id).ValueGeneratedNever();
            b.Property(u => u.Numero).HasConversion(n => n.Value, v => new NumeroUniteEtablissement(v)).HasColumnName("numero_ue_bce").HasMaxLength(10);
            b.HasIndex(u => u.Numero).IsUnique();
            b.Property(u => u.Nom).HasMaxLength(200);
            b.Property(u => u.Langue).HasConversion<string>().HasMaxLength(2);
            Adresse(b.ComplexProperty(u => u.Adresse));
            Validite(b.ComplexProperty(u => u.Validite));
            b.HasMany(u => u.Sites).WithOne().HasForeignKey("unite_etablissement_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(u => u.Sites).AutoInclude();
        });

        modelBuilder.Entity<Site>(b =>
        {
            b.ToTable("site");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).ValueGeneratedNever();
            b.Property(s => s.Nom).HasMaxLength(200);
            Adresse(b.ComplexProperty(s => s.Adresse));
            Validite(b.ComplexProperty(s => s.Validite));
            b.HasMany(s => s.Departements).WithOne().HasForeignKey("site_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(s => s.Departements).AutoInclude();
        });

        modelBuilder.Entity<Departement>(b =>
        {
            b.ToTable("departement");
            b.HasKey(d => d.Id);
            b.Property(d => d.Id).ValueGeneratedNever();
            b.Property(d => d.Nom).HasMaxLength(200);
            // Hiérarchie libre (AFF-02) : parent_id vers un département du même site.
            b.HasOne<Departement>().WithMany().HasForeignKey(d => d.ParentId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
            Validite(b.ComplexProperty(d => d.Validite));
        });

        modelBuilder.Entity<Contact>(b =>
        {
            b.ToTable("contact");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).ValueGeneratedNever();
            b.Property(c => c.Nom).HasMaxLength(200);
            b.Property(c => c.Fonction).HasMaxLength(200);
            b.Property(c => c.Role).HasConversion<string>().HasMaxLength(40);
            b.Property(c => c.Email).HasMaxLength(254);
            b.Property(c => c.Telephone).HasMaxLength(30);
            Validite(b.ComplexProperty(c => c.Validite));
        });

        modelBuilder.Entity<OrganeConcertation>(b =>
        {
            b.ToTable("organe_concertation");
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).ValueGeneratedNever();
            b.Property(o => o.Type).HasConversion<string>().HasMaxLength(30);
            Validite(b.ComplexProperty(o => o.Validite));
            b.HasMany(o => o.Reunions).WithOne().HasForeignKey("organe_concertation_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(o => o.Reunions).AutoInclude();
        });

        modelBuilder.Entity<ReunionConcertation>(b =>
        {
            b.ToTable("reunion_concertation");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).ValueGeneratedNever();
            // Référence vers le service Documents, sans clé étrangère physique (DAT-02).
            b.Property(r => r.OrdreDuJourDocumentId).HasColumnName("document_id");
        });

        modelBuilder.Entity<OperationAffilie>(b =>
        {
            b.ToTable("operation_affilie");
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).ValueGeneratedNever();
            b.Property(o => o.Type).HasConversion<string>().HasMaxLength(30);
            b.Property(o => o.Statut).HasConversion<string>().HasMaxLength(20);
            b.Property(o => o.SeppContrepartie).HasMaxLength(200);
            b.PrimitiveCollection(o => o.AffiliesBeneficiaires);
            b.HasIndex(o => o.AffilieAbsorbantId);
        });

        // AFF-05 — Historique en ajout seul, valeurs avant / après en JSON.
        modelBuilder.Entity<ModificationAffilie>(b =>
        {
            b.ToTable("modification_affilie");
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).ValueGeneratedNever();
            b.HasOne<Affilie>().WithMany().HasForeignKey(m => m.AffilieId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(m => new { m.AffilieId, m.NumeroVersion }).IsUnique();
            b.Property(m => m.Action).HasMaxLength(100);
            b.Property(m => m.Auteur).HasMaxLength(100);
            b.Property(m => m.Avant).HasColumnType("jsonb");
            b.Property(m => m.Apres).HasColumnType("jsonb");
        });

        // AFF-01, AFF-02, INT-04 — Écarts de synchronisation BCE à traiter par le gestionnaire de dossiers. Pas de clé
        // étrangère : l'écart existe aussi pour un numéro BCE sans affilié. Un seul écart ouvert par (BCE, code, référence).
        modelBuilder.Entity<EcartSynchronisation>(b =>
        {
            b.ToTable("ecart_synchronisation");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.NumeroBce).HasMaxLength(20);
            b.Property(e => e.Code).HasMaxLength(50);
            b.Property(e => e.Reference).HasMaxLength(100);
            b.Property(e => e.Detail).HasMaxLength(1000);
            b.Property(e => e.Statut).HasConversion<string>().HasMaxLength(20);
            b.Property(e => e.ResoluPar).HasMaxLength(100);
            b.HasIndex(e => new { e.NumeroBce, e.Code, e.Reference }).IsUnique().HasFilter("statut = 'Ouvert'");
            b.HasIndex(e => e.Statut);
        });
    }

    private static void Adresse(ComplexPropertyBuilder<Adresse> b)
    {
        b.Property(a => a.Rue).HasColumnName("adresse_rue").HasMaxLength(200);
        b.Property(a => a.Numero).HasColumnName("adresse_numero").HasMaxLength(20);
        b.Property(a => a.Boite).HasColumnName("adresse_boite").HasMaxLength(20);
        b.Property(a => a.CodePostal).HasColumnName("adresse_code_postal").HasMaxLength(20);
        b.Property(a => a.Localite).HasColumnName("adresse_localite").HasMaxLength(100);
        b.Property(a => a.CodePays).HasColumnName("adresse_code_pays").HasMaxLength(2);
    }

    /// <summary>Période de validité (DAT-04) : colonnes valid_from, valid_to.</summary>
    private static void Validite(ComplexPropertyBuilder<Validity> b)
    {
        b.Property(v => v.ValidFrom).HasColumnName("valid_from");
        b.Property(v => v.ValidTo).HasColumnName("valid_to");
        b.Ignore(v => v.IsOpen);
    }
}
