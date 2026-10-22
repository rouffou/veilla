using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.Reintegration.Domain;
using Sepp.Reintegration.Domain.Echanges;
using Sepp.Reintegration.Domain.Incapacites;
using Sepp.Reintegration.Domain.Projections;
using Sepp.Reintegration.Domain.Trajets;

namespace Sepp.Reintegration.Adapters.Persistence;

/// <summary>
/// Base propre au service Réintégration, en zone médicale (ARC-02, ARC-04, #272) : tables <c>incapacite</c>,
/// <c>estimation_potentiel</c>, <c>trajet</c>, <c>etape_trajet</c>, <c>absence_rdv</c>, <c>echange_ehealth</c> et projections.
/// Le contenu médical (commentaire d'estimation, note d'évaluation, contenu des échanges eHealth) est chiffré champ par champ
/// (AES-256-GCM) avec les clés de la zone médicale (ARC-45) : colonnes suffixées <c>_chiffre</c>. Restent en clair les
/// identifiants, dates, statuts et codes.
/// </summary>
public sealed class ReintegrationDbContext(
    DbContextOptions<ReintegrationDbContext> options,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ChiffrementZoneMedicale chiffrement)
    : SeppDbContext(options, currentUser, timeProvider)
{
    private const int CodeLength = 60;
    private const int UserLength = 100;
    private const int EnumLength = 40;
    private const int ChiffreLength = 100_000;

    public DbSet<Incapacite> Incapacites => Set<Incapacite>();

    public DbSet<Trajet> Trajets => Set<Trajet>();

    public DbSet<EchangeEHealth> Echanges => Set<EchangeEHealth>();

    public DbSet<ParametreLegalLocal> Parametres => Set<ParametreLegalLocal>();

    public DbSet<CalendrierLocal> Calendriers => Set<CalendrierLocal>();

    public DbSet<ObligationReintegration> Obligations => Set<ObligationReintegration>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        var texteChiffre = new EncryptedStringConverter(chiffrement.Encryptor);

        modelBuilder.Entity<Incapacite>(b =>
        {
            b.ToTable("incapacite");
            b.HasKey(i => i.Id);
            b.Property(i => i.Id).ValueGeneratedNever();
            b.Property(i => i.Source).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(i => i.Statut).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(i => i.GestionnaireId).HasMaxLength(UserLength);
            b.HasIndex(i => new { i.PersonneId, i.AffilieId });
            b.HasIndex(i => i.AffilieId);
            Minuteries(b);
            b.HasMany(i => i.Estimations).WithOne().HasForeignKey("incapacite_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(i => i.Estimations).AutoInclude().HasField("_estimations");
        });

        modelBuilder.Entity<EstimationPotentiel>(b =>
        {
            b.ToTable("estimation_potentiel");
            Cle(b);
            b.Property(e => e.Resultat).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(e => e.Methode).HasMaxLength(CodeLength);
            b.Property(e => e.AuteurId).HasMaxLength(UserLength);
            Chiffre(b.Property(e => e.Commentaire), texteChiffre, "commentaire_chiffre");
        });

        modelBuilder.Entity<Trajet>(b =>
        {
            b.ToTable("trajet");
            b.HasKey(t => t.Id);
            b.Property(t => t.Id).ValueGeneratedNever();
            b.Property(t => t.Initiateur).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(t => t.Statut).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(t => t.Conclusion).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(t => t.ReponseEmployeur).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(t => t.ReponseTravailleur).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(t => t.Issue).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(t => t.EvaluateurId).HasMaxLength(UserLength);
            Chiffre(b.Property(t => t.NoteEvaluation), texteChiffre, "note_evaluation_chiffre");
            b.HasIndex(t => t.IncapaciteId);
            b.HasIndex(t => new { t.PersonneId, t.AffilieId });
            b.HasIndex(t => t.AffilieId);
            Minuteries(b);
            b.HasMany(t => t.Etapes).WithOne().HasForeignKey("trajet_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(t => t.Etapes).AutoInclude().HasField("_etapes");
            b.HasMany(t => t.Absences).WithOne().HasForeignKey("trajet_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(t => t.Absences).AutoInclude().HasField("_absences");
        });

        modelBuilder.Entity<EtapeTrajet>(b =>
        {
            b.ToTable("etape_trajet");
            Cle(b);
            b.Property(e => e.Type).HasConversion<string>().HasMaxLength(EnumLength);
        });

        modelBuilder.Entity<AbsenceRendezVous>(b =>
        {
            b.ToTable("absence_rdv");
            Cle(b);
            b.Property(a => a.DateRendezVous).HasColumnName("date_rdv");
            b.HasIndex("trajet_id", nameof(AbsenceRendezVous.RendezVousId)).IsUnique().HasFilter("rendez_vous_id IS NOT NULL");
        });

        modelBuilder.Entity<EchangeEHealth>(b =>
        {
            b.ToTable("echange_ehealth");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Destinataire).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(e => e.Sens).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(e => e.Objet).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(e => e.Statut).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(e => e.ReferenceDestinataire).HasMaxLength(UserLength);
            b.Property(e => e.ReferenceEHealth).HasMaxLength(UserLength);
            b.Property(e => e.CodeErreur).HasMaxLength(UserLength);
            Chiffre(b.Property(e => e.Contenu), texteChiffre, "contenu_chiffre");
            b.HasIndex(e => e.ReferenceEHealth).IsUnique().HasFilter("reference_e_health IS NOT NULL");
            b.HasIndex(e => e.IncapaciteId);
            b.HasIndex(e => e.TrajetId);
            b.HasIndex(e => e.Statut);
        });

        modelBuilder.Entity<ParametreLegalLocal>(b =>
        {
            b.ToTable("parametre_legal_local");
            b.HasKey(p => new { p.Code, p.ValideDu });
            b.Property(p => p.Code).HasMaxLength(CodeLength);
            b.Property(p => p.Unite).HasMaxLength(EnumLength);
        });

        modelBuilder.Entity<CalendrierLocal>(b =>
        {
            b.ToTable("calendrier_local");
            b.HasKey(c => c.Annee);
            b.Property(c => c.Annee).ValueGeneratedNever();
            b.PrimitiveCollection(c => c.JoursSupplementaires);
        });

        modelBuilder.Entity<ObligationReintegration>(b =>
        {
            b.ToTable("obligation_reintegration");
            b.HasKey(o => o.ObligationId);
            b.Property(o => o.ObligationId).ValueGeneratedNever();
            b.Property(o => o.TypeExamen).HasMaxLength(CodeLength);
            b.HasIndex(o => o.PersonneId);
        });
    }

    private static void Minuteries<T>(EntityTypeBuilder<T> b)
        where T : ProcessusAEcheances
    {
        b.Property(p => p.ProchaineMinuterie).HasMaxLength(CodeLength);
        b.PrimitiveCollection(p => p.MinuteriesDeclenchees);
        b.HasIndex(p => p.ProchaineEcheance).HasFilter("prochaine_echeance IS NOT NULL");
    }

    private static void Cle<T>(EntityTypeBuilder<T> b)
        where T : Entity
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
    }

    /// <summary>Texte médical chiffré : la colonne ne contient que <c>v1:&lt;clé&gt;:…</c> (ARC-45).</summary>
    private static void Chiffre<T>(PropertyBuilder<T> propriete, ValueConverter convertisseur, string colonne) =>
        propriete.HasConversion(convertisseur).HasColumnName(colonne).HasMaxLength(ChiffreLength);
}

/// <summary>
/// Chiffrement applicatif propre à la zone médicale (ARC-04, ARC-45) : clés lues dans la section dédiée
/// <c>ZoneMedicale:Encryption</c> (<c>CurrentKeyId</c>, <c>Keys:&lt;id&gt;</c>), distinctes des clés des autres zones.
/// </summary>
public sealed class ChiffrementZoneMedicale(FieldEncryptor encryptor)
{
    public const string Section = "ZoneMedicale";

    public FieldEncryptor Encryptor { get; } = encryptor;
}
