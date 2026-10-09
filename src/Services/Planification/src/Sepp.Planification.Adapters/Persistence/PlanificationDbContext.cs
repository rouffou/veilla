using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Convocations;
using Sepp.Planification.Domain.Projections;
using Sepp.Planification.Domain.Ressources;
using Sepp.Planification.Domain.Sessions;

namespace Sepp.Planification.Adapters.Persistence;

/// <summary>
/// Base de données propre au service Planification (ARC-02). Les contraintes qui empêchent la double réservation sont
/// portées par la base (migration <c>Initial</c>) :
/// <list type="bullet">
/// <item><c>occupation_ressource</c> : contrainte d'exclusion (<c>EXCLUDE USING gist</c>) — une ressource (conseiller,
/// salle, appareil, unité mobile) ne peut pas être occupée par deux créneaux dont les périodes se chevauchent ;</item>
/// <item><c>rendez_vous</c> : index unique partiel — un créneau n'a qu'un rendez-vous actif ;</item>
/// <item>verrou optimiste (DAT-03) sur le créneau : deux réservations concurrentes du même créneau, la seconde échoue (409).</item>
/// </list>
/// Aucune donnée de santé : un rendez-vous ne porte que des identifiants, des horaires et un type d'acte (catégorie).
/// </summary>
public sealed class PlanificationDbContext(DbContextOptions<PlanificationDbContext> options, ICurrentUser currentUser, TimeProvider timeProvider)
    : SeppDbContext(options, currentUser, timeProvider)
{
    /// <summary>Statuts d'un rendez-vous qui occupent son créneau (filtre de l'index unique partiel).</summary>
    public const string FiltreRendezVousActif = "statut IN ('Planifie', 'Arrive', 'EnSalle')";

    public DbSet<Lieu> Lieux => Set<Lieu>();

    public DbSet<Ressource> Ressources => Set<Ressource>();

    public DbSet<Absence> Absences => Set<Absence>();

    public DbSet<ModeleAgenda> ModelesAgenda => Set<ModeleAgenda>();

    public DbSet<DureeStandard> DureesStandard => Set<DureeStandard>();

    public DbSet<Creneau> Creneaux => Set<Creneau>();

    public DbSet<RendezVous> RendezVous => Set<RendezVous>();

    public DbSet<Convocation> Convocations => Set<Convocation>();

    public DbSet<PreferenceConvocation> PreferencesConvocation => Set<PreferenceConvocation>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<ObligationAPlanifier> Obligations => Set<ObligationAPlanifier>();

    public DbSet<ParametreLegalLocal> ParametresLegaux => Set<ParametreLegalLocal>();

    public DbSet<CalendrierLocal> Calendriers => Set<CalendrierLocal>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Lieu>(b =>
        {
            b.ToTable("lieu");
            b.HasKey(l => l.Id);
            b.Property(l => l.Id).ValueGeneratedNever();
            b.Property(l => l.Type).HasConversion<string>().HasMaxLength(30);
            b.Property(l => l.Nom).HasMaxLength(200);
            b.Property(l => l.Adresse).HasMaxLength(300);
            b.Property(l => l.CodePostal).HasMaxLength(10);
            b.Ignore(l => l.Position);
            b.HasIndex(l => l.AffilieId);
        });

        modelBuilder.Entity<Ressource>(b =>
        {
            b.ToTable("ressource");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).ValueGeneratedNever();
            b.Property(r => r.Type).HasConversion<string>().HasMaxLength(30);
            b.Property(r => r.Libelle).HasMaxLength(200);
            b.Property(r => r.ReferenceId).HasMaxLength(100);
            b.Property(r => r.FournisseurAgenda).HasConversion<string>().HasMaxLength(30);
            b.Property(r => r.CompteAgenda).HasMaxLength(200);
            b.Ignore(r => r.EstHumaine);
            b.HasOne<Lieu>().WithMany().HasForeignKey(r => r.LieuId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(r => r.ReferenceId);
        });

        modelBuilder.Entity<Absence>(b =>
        {
            b.ToTable("absence");
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).ValueGeneratedNever();
            b.Property(a => a.Source).HasConversion<string>().HasMaxLength(30);
            b.Property(a => a.ReferenceExterne).HasMaxLength(200);
            b.HasOne<Ressource>().WithMany().HasForeignKey(a => a.RessourceId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(a => new { a.RessourceId, a.Debut });

            // Réimport idempotent : une référence de source est unique par source.
            b.HasIndex(a => new { a.Source, a.ReferenceExterne }).IsUnique().HasFilter("reference_externe IS NOT NULL");
        });

        modelBuilder.Entity<ModeleAgenda>(b =>
        {
            b.ToTable("modele_agenda");
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).ValueGeneratedNever();
            Validite(b.ComplexProperty(m => m.Validite));
            b.Ignore(m => m.JoursPresence);
            b.HasOne<Ressource>().WithMany().HasForeignKey(m => m.RessourceId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Lieu>().WithMany().HasForeignKey(m => m.LieuId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(m => m.Plages).WithOne().HasForeignKey("modele_agenda_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(m => m.Plages).AutoInclude();
            b.HasIndex(m => m.RessourceId);
        });

        modelBuilder.Entity<PlageModele>(b =>
        {
            b.ToTable("plage_modele");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).ValueGeneratedNever();
            b.Property(p => p.Jour).HasConversion<string>().HasMaxLength(10);
            b.Property(p => p.TypeActe).HasMaxLength(50);
        });

        modelBuilder.Entity<DureeStandard>(b =>
        {
            b.ToTable("duree_standard");
            b.HasKey(d => d.Id);
            b.Property(d => d.Id).ValueGeneratedNever();
            b.Property(d => d.TypeActe).HasMaxLength(50);
            b.HasOne<Ressource>().WithMany().HasForeignKey(d => d.RessourceId).OnDelete(DeleteBehavior.Restrict);

            // PLA-03 : une durée par type d'acte, globale ou propre à une ressource (NULL distincts en SQL : deux index).
            b.HasIndex(d => d.TypeActe).IsUnique().HasFilter("ressource_id IS NULL").HasDatabaseName("ux_duree_standard_globale");
            b.HasIndex(d => new { d.TypeActe, d.RessourceId }).IsUnique().HasFilter("ressource_id IS NOT NULL").HasDatabaseName("ux_duree_standard_ressource");
        });

        modelBuilder.Entity<Creneau>(b =>
        {
            b.ToTable("creneau");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).ValueGeneratedNever();
            b.Property(c => c.TypeActe).HasMaxLength(50);
            b.Property(c => c.Statut).HasConversion<string>().HasMaxLength(20);
            b.Ignore(c => c.RessourcesMobilisees);
            b.HasOne<Ressource>().WithMany().HasForeignKey(c => c.RessourceId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Lieu>().WithMany().HasForeignKey(c => c.LieuId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ModeleAgenda>().WithMany().HasForeignKey(c => c.ModeleAgendaId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Session>().WithMany().HasForeignKey(c => c.SessionId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(c => c.Occupations).WithOne().HasForeignKey("creneau_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(c => c.Occupations).AutoInclude();
            b.HasIndex(c => new { c.RessourceId, c.Debut });
            b.HasIndex(c => new { c.Statut, c.Debut });
            b.HasIndex(c => c.SessionId);
        });

        // La contrainte d'exclusion (btree_gist) est ajoutée en SQL dans la migration Initial : EF ne sait pas l'exprimer.
        modelBuilder.Entity<OccupationRessource>(b =>
        {
            b.ToTable("occupation_ressource");
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).ValueGeneratedNever();
            b.HasOne<Ressource>().WithMany().HasForeignKey(o => o.RessourceId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(o => new { o.RessourceId, o.Debut });
        });

        modelBuilder.Entity<RendezVous>(b =>
        {
            b.ToTable("rendez_vous");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).ValueGeneratedNever();
            b.Property(r => r.TypeActe).HasMaxLength(50);
            b.Property(r => r.Statut).HasConversion<string>().HasMaxLength(20);
            b.Property(r => r.MotifAnnulation).HasConversion<string>().HasMaxLength(30);
            b.Property(r => r.Origine).HasConversion<string>().HasMaxLength(30);
            b.Property(r => r.ReferenceAgendaExterne).HasMaxLength(200);
            b.Ignore(r => r.EstActif);
            b.HasOne<Creneau>().WithMany().HasForeignKey(r => r.CreneauId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(r => new { r.PersonneId, r.Debut });
            b.HasIndex(r => new { r.AffilieId, r.Debut });
            b.HasIndex(r => new { r.RessourceId, r.Debut });
            b.HasIndex(r => new { r.LieuId, r.Debut });

            // Un créneau n'a qu'un rendez-vous actif : deuxième filet contre la double réservation.
            b.HasIndex(r => r.CreneauId).IsUnique().HasFilter(FiltreRendezVousActif).HasDatabaseName("ux_rendez_vous_creneau_actif");
        });

        modelBuilder.Entity<Convocation>(b =>
        {
            b.ToTable("convocation");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).ValueGeneratedNever();
            b.Property(c => c.Canal).HasConversion<string>().HasMaxLength(20);
            b.Property(c => c.Type).HasConversion<string>().HasMaxLength(30);
            b.Property(c => c.MessageId).HasMaxLength(100);
            b.HasOne<RendezVous>().WithMany().HasForeignKey(c => c.RendezVousId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(c => c.RendezVousId);
            b.HasIndex(c => c.LotId);
        });

        modelBuilder.Entity<PreferenceConvocation>(b =>
        {
            b.ToTable("preference_convocation");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).ValueGeneratedNever();
            b.Property(p => p.Canal).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(p => p.AffilieId).IsUnique();
        });

        modelBuilder.Entity<Session>(b =>
        {
            b.ToTable("session");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).ValueGeneratedNever();
            b.Property(s => s.TypeActe).HasMaxLength(50);
            b.Property(s => s.Statut).HasConversion<string>().HasMaxLength(20);
            b.Ignore(s => s.EstTournee);
            b.HasOne<Lieu>().WithMany().HasForeignKey(s => s.LieuId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Ressource>().WithMany().HasForeignKey(s => s.ConseillerId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(s => s.Itineraire).WithOne().HasForeignKey("session_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(s => s.Itineraire).AutoInclude();
            b.HasIndex(s => s.Date);
        });

        modelBuilder.Entity<EtapeTournee>(b =>
        {
            b.ToTable("etape_tournee");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Emplacement).HasMaxLength(300);
        });

        // Modèle de lecture des obligations (PLA-04) : projection d'événements, pas un agrégat.
        modelBuilder.Entity<ObligationAPlanifier>(b =>
        {
            b.ToTable("obligation_a_planifier");
            b.HasKey(o => o.ObligationId);
            b.Property(o => o.ObligationId).ValueGeneratedNever();
            b.Property(o => o.TypeExamen).HasMaxLength(100);
            b.Ignore(o => o.EstAPlanifier);
            b.Property(o => o.StatutCloture).HasMaxLength(30);
            b.HasIndex(o => new { o.AffilieId, o.DateDue });
            b.HasIndex(o => o.PersonneId);
            b.HasIndex(o => o.RendezVousId);
        });

        // DAT-08 : jours fériés supplémentaires par année (referentiels.jours-feries-modifies).
        modelBuilder.Entity<CalendrierLocal>(b =>
        {
            b.ToTable("calendrier_local");
            b.HasKey(c => c.Annee);
            b.Property(c => c.Annee).ValueGeneratedNever();
            b.PrimitiveCollection(c => c.JoursSupplementaires);
        });

        // ARC-21 : copie locale des paramètres légaux (délais d'urgence, rappels).
        modelBuilder.Entity<ParametreLegalLocal>(b =>
        {
            b.ToTable("parametre_legal_local");
            b.HasKey(p => new { p.Code, p.ValideDu });
            b.Property(p => p.Code).HasMaxLength(100);
            b.Property(p => p.Unite).HasMaxLength(30);
            b.Property(p => p.Valeur).HasPrecision(18, 4);
        });
    }

    /// <summary>Période de validité (DAT-04) : colonnes valid_from, valid_to.</summary>
    private static void Validite(ComplexPropertyBuilder<Validity> b)
    {
        b.Property(v => v.ValidFrom).HasColumnName("valid_from");
        b.Property(v => v.ValidTo).HasColumnName("valid_to");
        b.Ignore(v => v.IsOpen);
    }
}
