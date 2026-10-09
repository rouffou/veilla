using Microsoft.EntityFrameworkCore;

using Npgsql;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.Obligations.Application;
using Sepp.Obligations.Domain.Demandes;
using Sepp.Obligations.Domain.Obligations;
using Sepp.Obligations.Domain.Projections;
using Sepp.Obligations.Domain.Reprises;

namespace Sepp.Obligations.Adapters.Persistence;

/// <summary>Base de données propre au service Obligations (ARC-02) : obligations du §15.3 et modèles de lecture alimentés par événements.</summary>
public sealed class ObligationsDbContext(DbContextOptions<ObligationsDbContext> options, ICurrentUser currentUser, TimeProvider timeProvider)
    : SeppDbContext(options, currentUser, timeProvider)
{
    private const int CodeLength = 100;
    private const int EnumLength = 40;
    private const int UserLength = 100;

    public DbSet<Obligation> Obligations => Set<Obligation>();

    public DbSet<DemandeTravailleur> Demandes => Set<DemandeTravailleur>();

    public DbSet<AffectationLocale> Affectations => Set<AffectationLocale>();

    public DbSet<ProfilRisquePosteLocal> Profils => Set<ProfilRisquePosteLocal>();

    public DbSet<RegleSurveillanceLocale> Regles => Set<RegleSurveillanceLocale>();

    public DbSet<SurchargeFrequenceLocale> Surcharges => Set<SurchargeFrequenceLocale>();

    public DbSet<OccupationLocale> Occupations => Set<OccupationLocale>();

    public DbSet<EtatParticulierLocal> EtatsParticuliers => Set<EtatParticulierLocal>();

    public DbSet<ExamenLocal> Examens => Set<ExamenLocal>();

    public DbSet<ParametreLegalLocal> Parametres => Set<ParametreLegalLocal>();

    public DbSet<CalendrierLocal> Calendriers => Set<CalendrierLocal>();

    public DbSet<RendezVousLocal> RendezVous => Set<RendezVousLocal>();

    public DbSet<RepriseLocale> Reprises => Set<RepriseLocale>();

    public DbSet<IncapaciteLocale> Incapacites => Set<IncapaciteLocale>();

    public DbSet<TrajetLocal> Trajets => Set<TrajetLocal>();

    public DbSet<ListeNominativeLocale> ListesNominatives => Set<ListeNominativeLocale>();

    public DbSet<ProcessusReprise> Processus => Set<ProcessusReprise>();

    public DbSet<DecisionRecue> DecisionsRecues => Set<DecisionRecue>();

    /// <summary>Index d'unicité du processus de reprise actif : départage deux annonces simultanées (ARC-33).</summary>
    public const string IndexProcessusActif = "ux_processus_reprise_actif";

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: IndexProcessusActif })
        {
            throw new DoublonProcessusRepriseException(ex);
        }
    }

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        ConfigurerObligations(modelBuilder);
        ConfigurerProjections(modelBuilder);
    }

    private static void ConfigurerObligations(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Obligation>(b =>
        {
            b.ToTable("obligation");
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).ValueGeneratedNever();
            b.Property(o => o.Cle).HasMaxLength(Obligation.LongueurCle);
            b.HasIndex(o => new { o.PersonneId, o.Cle }).IsUnique();
            b.HasIndex(o => new { o.AffilieId, o.Statut });
            b.Property(o => o.Type).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(o => o.Origine).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(o => o.Statut).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(o => o.MotifAnnulation).HasConversion<string>().HasMaxLength(EnumLength);
            b.PrimitiveCollection(o => o.CodesRisques);
            b.HasMany(o => o.Traces).WithOne().HasForeignKey("obligation_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(o => o.Traces).AutoInclude();
            b.Ignore(o => o.EstOuverte);
            b.Ignore(o => o.Echeance);
        });

        modelBuilder.Entity<TraceCalcul>(b =>
        {
            b.ToTable("trace_calcul");
            b.HasKey(t => t.Id);
            b.Property(t => t.Id).ValueGeneratedNever();
            b.Property(t => t.Regle).HasMaxLength(200);
            b.Property(t => t.Explication).HasMaxLength(1000);
            b.PrimitiveCollection(t => t.Entrees);
        });

        modelBuilder.Entity<DemandeTravailleur>(b =>
        {
            b.ToTable("demande_travailleur");
            b.HasKey(d => d.Id);
            b.Property(d => d.Id).ValueGeneratedNever();
            b.HasIndex(d => d.PersonneId);
            b.Property(d => d.Type).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(d => d.EnregistreePar).HasMaxLength(UserLength);
        });
    }

    private static void ConfigurerProjections(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AffectationLocale>(b =>
        {
            b.ToTable("affectation_locale");
            b.HasKey(a => a.AffectationId);
            b.Property(a => a.AffectationId).ValueGeneratedNever();
            b.HasIndex(a => a.PersonneId);
            b.HasIndex(a => a.PosteId);
        });

        modelBuilder.Entity<ProfilRisquePosteLocal>(b =>
        {
            b.ToTable("profil_risque_poste_local");
            b.HasKey(p => new { p.PosteId, p.ValideDu });
            b.PrimitiveCollection(p => p.CodesRisques);
        });

        modelBuilder.Entity<RegleSurveillanceLocale>(b =>
        {
            b.ToTable("regle_surveillance_locale");
            b.HasKey(r => new { r.CodeRisque, r.Version });
            b.Property(r => r.CodeRisque).HasMaxLength(CodeLength);
            b.Property(r => r.Categorie).HasMaxLength(EnumLength);
            b.Property(r => r.TypeSurveillance).HasMaxLength(CodeLength);
        });

        modelBuilder.Entity<SurchargeFrequenceLocale>(b =>
        {
            b.ToTable("surcharge_frequence_locale");
            b.HasKey(s => s.SurchargeId);
            b.Property(s => s.SurchargeId).ValueGeneratedNever();
            b.HasIndex(s => s.CibleId);
            b.Property(s => s.CibleType).HasMaxLength(EnumLength);
            b.Property(s => s.CodeRisque).HasMaxLength(CodeLength);
        });

        modelBuilder.Entity<OccupationLocale>(b =>
        {
            b.ToTable("occupation_locale");
            b.HasKey(o => o.OccupationId);
            b.Property(o => o.OccupationId).ValueGeneratedNever();
            b.HasIndex(o => o.PersonneId);
            b.HasIndex(o => o.AffilieId);
        });

        modelBuilder.Entity<EtatParticulierLocal>(b =>
        {
            b.ToTable("etat_particulier_local");
            b.HasKey(e => e.EtatParticulierId);
            b.Property(e => e.EtatParticulierId).ValueGeneratedNever();
            b.HasIndex(e => e.PersonneId);
            b.Property(e => e.Categorie).HasMaxLength(CodeLength);
            b.Ignore(e => e.EstProtectionMaternite);
        });

        modelBuilder.Entity<ExamenLocal>(b =>
        {
            b.ToTable("examen_local");
            b.HasKey(e => e.ExamenId);
            b.Property(e => e.ExamenId).ValueGeneratedNever();
            b.HasIndex(e => e.PersonneId);
            b.Property(e => e.TypeExamen).HasMaxLength(CodeLength);
        });

        modelBuilder.Entity<ParametreLegalLocal>(b =>
        {
            b.ToTable("parametre_legal_local");
            b.HasKey(p => new { p.Code, p.ValideDu });
            b.Property(p => p.Code).HasMaxLength(CodeLength);
            b.Property(p => p.Unite).HasMaxLength(EnumLength);
            b.Property(p => p.Valeur).HasPrecision(18, 4);
        });

        modelBuilder.Entity<CalendrierLocal>(b =>
        {
            b.ToTable("calendrier_local");
            b.HasKey(c => c.Annee);
            b.Property(c => c.Annee).ValueGeneratedNever();
            b.PrimitiveCollection(c => c.JoursSupplementaires);
        });

        modelBuilder.Entity<RendezVousLocal>(b =>
        {
            b.ToTable("rendez_vous_local");
            b.HasKey(r => r.RendezVousId);
            b.Property(r => r.RendezVousId).ValueGeneratedNever();
            b.HasIndex(r => r.PersonneId);
            b.PrimitiveCollection(r => r.ObligationIds);
            b.Property(r => r.MotifAnnulation).HasMaxLength(CodeLength);
            b.Ignore(r => r.EstActif);
            b.Ignore(r => r.ConvocationAJour);
            b.Ignore(r => r.ConvocationNonRemise);
        });

        modelBuilder.Entity<RepriseLocale>(b =>
        {
            b.ToTable("reprise_locale");
            b.HasKey(r => new { r.PersonneId, r.AffilieId, r.DateReprise });
        });

        modelBuilder.Entity<ProcessusReprise>(b =>
        {
            b.ToTable("processus_reprise");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).ValueGeneratedNever();
            b.Property(p => p.Origine).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(p => p.Statut).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(p => p.TypeMinuterie).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(p => p.MotifReplanification).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(p => p.MotifAnnulation).HasMaxLength(CodeLength);
            b.PrimitiveCollection(p => p.RendezVousAbsents);
            b.PrimitiveCollection(p => p.MinuteriesDeclenchees);
            b.Ignore(p => p.NombreAbsences);
            b.Ignore(p => p.EstAnnulee);
            b.Ignore(p => p.EstActif);
            b.Ignore(p => p.ConvocationNonRemise);

            // Un seul processus actif par travailleur, affilié et date de reprise (hors annulés) : idempotence des annonces simultanées.
            b.HasIndex(p => new { p.PersonneId, p.AffilieId, p.DateReprise }).IsUnique().HasFilter("annulee_le IS NULL").HasDatabaseName(IndexProcessusActif);
            b.HasIndex(p => p.PersonneId);
            b.HasIndex(p => p.ObligationId);
            b.HasIndex(p => p.ExamenId);
            b.HasIndex(p => p.DecisionId);
            b.HasIndex(p => p.AffilieId);
            b.HasIndex(p => p.ProchaineEcheance).HasFilter("prochaine_echeance IS NOT NULL");
        });

        modelBuilder.Entity<DecisionRecue>(b =>
        {
            b.ToTable("decision_recue");
            b.HasKey(d => d.ExamenId);
            b.Property(d => d.ExamenId).ValueGeneratedNever();
        });

        modelBuilder.Entity<IncapaciteLocale>(b =>
        {
            b.ToTable("incapacite_locale");
            b.HasKey(i => i.IncapaciteId);
            b.Property(i => i.IncapaciteId).ValueGeneratedNever();
            b.HasIndex(i => i.PersonneId);
            b.Property(i => i.Source).HasMaxLength(EnumLength);
        });

        modelBuilder.Entity<TrajetLocal>(b =>
        {
            b.ToTable("trajet_local");
            b.HasKey(t => t.TrajetId);
            b.Property(t => t.TrajetId).ValueGeneratedNever();
            b.HasIndex(t => t.PersonneId);
            b.Property(t => t.Statut).HasMaxLength(EnumLength);
        });

        modelBuilder.Entity<ListeNominativeLocale>(b =>
        {
            b.ToTable("liste_nominative_locale");
            b.HasKey(l => l.ListeNominativeId);
            b.Property(l => l.ListeNominativeId).ValueGeneratedNever();
            b.HasIndex(l => l.AffilieId);
            b.Property(l => l.TypeListe).HasMaxLength(EnumLength);
        });
    }
}
