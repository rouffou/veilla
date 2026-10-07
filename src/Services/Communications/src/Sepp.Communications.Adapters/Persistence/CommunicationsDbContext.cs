using Microsoft.EntityFrameworkCore;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.Communications.Domain.Messages;

namespace Sepp.Communications.Adapters.Persistence;

/// <summary>
/// Base propre au service Communications (ARC-02) : journal des messages et preuves d'envoi (DOC-05). Aucune coordonnée de
/// destinataire n'est conservée : elles sont lues auprès des services propriétaires au moment de l'envoi.
/// </summary>
public sealed class CommunicationsDbContext(DbContextOptions<CommunicationsDbContext> options, ICurrentUser currentUser, TimeProvider timeProvider)
    : SeppDbContext(options, currentUser, timeProvider)
{
    public DbSet<Message> Messages => Set<Message>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Message>(b =>
        {
            b.ToTable("message");
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).ValueGeneratedNever();
            b.Property(m => m.Type).HasConversion<string>().HasMaxLength(30);
            b.Property(m => m.Canal).HasConversion<string>().HasMaxLength(30);
            b.Property(m => m.TypeDestinataire).HasConversion<string>().HasMaxLength(20);
            b.Property(m => m.Langue).HasConversion<string>().HasMaxLength(2);
            b.Property(m => m.ObjetType).HasMaxLength(50);
            b.Property(m => m.CleIdempotence).HasMaxLength(200);
            b.Property(m => m.Sujet).HasMaxLength(ContenuMessage.LongueurMaximaleSujet);
            b.Property(m => m.Corps).HasMaxLength(ContenuMessage.LongueurMaximaleCorps);
            b.Property(m => m.Statut).HasConversion<string>().HasMaxLength(20);
            b.Property(m => m.DerniereErreur).HasMaxLength(100);

            // DOC-03 : envoi idempotent. La contrainte tranche les traitements concurrents d'un même fait métier.
            b.HasIndex(m => m.CleIdempotence).IsUnique();
            b.HasIndex(m => new { m.Statut, m.ProchaineTentative }).HasFilter("prochaine_tentative IS NOT NULL");
            b.HasIndex(m => new { m.DestinataireId, m.CreeLe });
            b.HasIndex(m => new { m.ObjetType, m.ObjetId });
            b.HasMany(m => m.Preuves).WithOne().HasForeignKey("message_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(m => m.Preuves).AutoInclude();
        });

        modelBuilder.Entity<PreuveEnvoi>(b =>
        {
            b.ToTable("preuve_envoi");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).ValueGeneratedNever();
            b.Property(p => p.Type).HasMaxLength(50);
            b.Property(p => p.Reference).HasMaxLength(500);
            b.Property(p => p.Empreinte).HasMaxLength(64).IsFixedLength();
        });
    }
}
