using Microsoft.EntityFrameworkCore;

using Sepp.Audit.Domain.Journal;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Persistence;

namespace Sepp.Audit.Adapters.Persistence;

/// <summary>
/// Base de données propre au service Audit (ARC-02). Les tables <c>entree_audit</c> et <c>sceau_purge</c> sont en
/// ajout seul : des déclencheurs PostgreSQL, créés par la migration initiale, refusent toute modification et toute
/// suppression hors purge légale (NF-04).
/// </summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options, ICurrentUser currentUser, TimeProvider timeProvider)
    : SeppDbContext(options, currentUser, timeProvider)
{
    public DbSet<EntreeAudit> Entrees => Set<EntreeAudit>();

    public DbSet<SceauPurge> Sceaux => Set<SceauPurge>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EntreeAudit>(b =>
        {
            b.ToTable("entree_audit");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Zone).HasConversion<string>().HasMaxLength(20);
            b.Property(e => e.Action).HasConversion<string>().HasMaxLength(20);
            b.Property(e => e.UtilisateurId).HasMaxLength(EntreeAudit.LongueurMaximaleUtilisateur);
            b.Property(e => e.Role).HasMaxLength(EntreeAudit.LongueurMaximaleRole);
            b.Property(e => e.Service).HasMaxLength(EntreeAudit.LongueurMaximaleService);
            b.Property(e => e.ObjetType).HasMaxLength(EntreeAudit.LongueurMaximaleObjetType);
            b.Property(e => e.Motif).HasMaxLength(EntreeAudit.LongueurMaximaleMotif);
            b.Property(e => e.EmpreintePrecedente).HasMaxLength(64).IsFixedLength();
            b.Property(e => e.Empreinte).HasMaxLength(64).IsFixedLength();
            b.Ignore(e => e.Maillon);

            // Une chaîne par zone, sans doublon de rang ; un seul enregistrement par événement reçu (ARC-31).
            b.HasIndex(e => new { e.Zone, e.Numero }).IsUnique();
            b.HasIndex(e => e.EvenementSourceId).IsUnique();
            b.HasIndex(e => new { e.UtilisateurId, e.Horodatage });
            b.HasIndex(e => new { e.ObjetType, e.ObjetId });
            b.HasIndex(e => new { e.Zone, e.Horodatage });
            b.HasIndex(e => e.Horodatage).HasFilter("bris_de_glace");
        });

        modelBuilder.Entity<SceauPurge>(b =>
        {
            b.ToTable("sceau_purge");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
            b.Property(s => s.Zone).HasConversion<string>().HasMaxLength(20);
            b.Property(s => s.EmpreinteFinale).HasMaxLength(64).IsFixedLength();
            b.Ignore(s => s.Maillon);
            b.HasIndex(s => new { s.Zone, s.NumeroFinal }).IsUnique();
        });
    }
}
