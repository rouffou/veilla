using Microsoft.EntityFrameworkCore;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.Documents.Domain.Documents;
using Sepp.Documents.Domain.Modeles;

namespace Sepp.Documents.Adapters.Persistence;

/// <summary>
/// Base propre au service Documents (ARC-02) : modèles versionnés, métadonnées des documents archivés et signatures.
/// Le contenu des documents n'est jamais en base (stockage objet chiffré par zone).
/// </summary>
public sealed class DocumentsDbContext(DbContextOptions<DocumentsDbContext> options, ICurrentUser currentUser, TimeProvider timeProvider)
    : SeppDbContext(options, currentUser, timeProvider)
{
    public DbSet<Modele> Modeles => Set<Modele>();

    public DbSet<Document> Documents => Set<Document>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Modele>(b =>
        {
            b.ToTable("modele");
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).ValueGeneratedNever();
            b.Property(m => m.Code).HasMaxLength(100);
            b.Property(m => m.Langue).HasConversion<string>().HasMaxLength(2);
            b.Property(m => m.Type).HasConversion<string>().HasMaxLength(20);
            b.Property(m => m.Zone).HasConversion<string>().HasMaxLength(20);
            b.Property(m => m.Statut).HasConversion<string>().HasMaxLength(20);
            b.Property(m => m.Libelle).HasMaxLength(Modele.LongueurMaximaleLibelle);
            b.Property(m => m.Description).HasMaxLength(Modele.LongueurMaximaleDescription);
            b.Property(m => m.Contenu);
            b.Property(m => m.ValidePar).HasMaxLength(100);
            b.Property(m => m.PubliePar).HasMaxLength(100);
            b.Ignore(m => m.Definitions);
            // DOC-01 : une seule version publiée par code et par langue. Deux publications concurrentes retirent la même
            // version précédente : le verrou optimiste de celle-ci (DAT-03) fait échouer la seconde (409).
            b.HasIndex(m => new { m.Code, m.Langue, m.Version }).IsUnique();
            b.HasMany(m => m.Champs).WithOne().HasForeignKey("modele_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(m => m.Champs).AutoInclude();
        });

        modelBuilder.Entity<ChampModele>(b =>
        {
            b.ToTable("champ_modele");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).ValueGeneratedNever();
            b.Property(c => c.Nom).HasMaxLength(50);
            b.Property(c => c.TypeChamp).HasConversion<string>().HasMaxLength(20);
            b.Property(c => c.Libelle).HasMaxLength(Modele.LongueurMaximaleLibelle);
            b.Ignore(c => c.Definition);
            b.HasIndex("modele_id", nameof(ChampModele.Nom)).IsUnique();
        });

        modelBuilder.Entity<Document>(b =>
        {
            b.ToTable("document");
            b.HasKey(d => d.Id);
            b.Property(d => d.Id).ValueGeneratedNever();
            b.Property(d => d.CodeModele).HasMaxLength(100);
            b.Property(d => d.Langue).HasConversion<string>().HasMaxLength(2);
            b.Property(d => d.MotifLangue).HasMaxLength(200);
            b.Property(d => d.Zone).HasConversion<string>().HasMaxLength(20);
            b.Property(d => d.ServiceProprietaire).HasMaxLength(100);
            b.Property(d => d.ObjetType).HasMaxLength(100);
            b.Property(d => d.TypeDestinataire).HasConversion<string>().HasMaxLength(20);
            b.Property(d => d.Exemplaire).HasMaxLength(50);
            b.Property(d => d.CleIdempotence).HasMaxLength(200);
            b.Property(d => d.StockageUri).HasMaxLength(1000);
            b.Property(d => d.Empreinte).HasMaxLength(64).IsFixedLength();
            b.Property(d => d.EmpreinteStockage).HasMaxLength(64).IsFixedLength();
            b.Property(d => d.Format).HasMaxLength(20);
            b.Property(d => d.CleChiffrement).HasMaxLength(100);
            b.Property(d => d.AutoriteHorodatage).HasMaxLength(200);
            b.Property(d => d.JetonHorodatage);
            b.Property(d => d.Statut).HasConversion<string>().HasMaxLength(20);
            b.HasOne<Modele>().WithMany().HasForeignKey(d => d.ModeleId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(d => d.CleIdempotence).IsUnique().HasFilter("cle_idempotence IS NOT NULL");
            b.HasIndex(d => new { d.ObjetType, d.ObjetId });
            b.HasIndex(d => new { d.TypeDestinataire, d.DestinataireId });
            b.HasMany(d => d.Signatures).WithOne().HasForeignKey("document_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(d => d.Signatures).AutoInclude();
        });

        modelBuilder.Entity<Signature>(b =>
        {
            b.ToTable("signature");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).ValueGeneratedNever();
            b.Property(s => s.SignataireId).HasMaxLength(100);
            b.Property(s => s.Type).HasMaxLength(50);
            b.Property(s => s.Preuve).HasMaxLength(8_000);
            b.Property(s => s.EmpreinteSignee).HasMaxLength(64).IsFixedLength();
            b.HasIndex("document_id", nameof(Signature.SignataireId)).IsUnique();
        });
    }
}
