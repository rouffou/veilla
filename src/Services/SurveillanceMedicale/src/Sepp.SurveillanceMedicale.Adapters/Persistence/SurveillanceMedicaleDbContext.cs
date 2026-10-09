using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.SurveillanceMedicale.Domain.Conservation;
using Sepp.SurveillanceMedicale.Domain.Decisions;
using Sepp.SurveillanceMedicale.Domain.Dossiers;
using Sepp.SurveillanceMedicale.Domain.Examens;
using Sepp.SurveillanceMedicale.Domain.MaladiesProfessionnelles;
using Sepp.SurveillanceMedicale.Domain.Projections;
using Sepp.SurveillanceMedicale.Domain.Protocoles;
using Sepp.SurveillanceMedicale.Domain.Transferts;
using Sepp.SurveillanceMedicale.Domain.Vaccinations;

namespace Sepp.SurveillanceMedicale.Adapters.Persistence;

/// <summary>
/// Base de données propre au service Surveillance médicale, en zone médicale (ARC-02, ARC-04). Tout le contenu clinique
/// est chiffré champ par champ (AES-256-GCM) avec les clés de la zone médicale (ARC-45) : colonnes suffixées
/// <c>_chiffre</c>, qui ne contiennent que le texte chiffré <c>v1:&lt;clé&gt;:…</c>. Restent en clair les identifiants,
/// dates, statuts, codes de référentiel et la catégorie de décision (qui sort de la zone, ARC-06).
/// </summary>
/// <remarks>
/// Les convertisseurs de chiffrement sont construits avec le <see cref="FieldEncryptor"/> du premier contexte (modèle EF
/// mis en cache par processus) : les clés de la zone médicale proviennent de la configuration, identique pour le processus.
/// </remarks>
public sealed class SurveillanceMedicaleDbContext(
    DbContextOptions<SurveillanceMedicaleDbContext> options,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ChiffrementZoneMedicale chiffrement)
    : SeppDbContext(options, currentUser, timeProvider)
{
    private const int CodeLength = 60;
    private const int UserLength = 100;
    private const int EnumLength = 40;
    private const int ChiffreLength = 100_000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public DbSet<DossierSante> Dossiers => Set<DossierSante>();

    public DbSet<Examen> Examens => Set<Examen>();

    public DbSet<Decision> Decisions => Set<Decision>();

    public DbSet<ValeurReference> ValeursReference => Set<ValeurReference>();

    public DbSet<SchemaVaccinal> SchemasVaccinaux => Set<SchemaVaccinal>();

    public DbSet<ModeleQuestionnaire> ModelesQuestionnaire => Set<ModeleQuestionnaire>();

    public DbSet<ModeleTexte> ModelesTexte => Set<ModeleTexte>();

    public DbSet<DureeConservationExposition> DureesConservation => Set<DureeConservationExposition>();

    public DbSet<LotVaccin> LotsVaccins => Set<LotVaccin>();

    public DbSet<DeclarationMaladieProfessionnelle> DeclarationsMp => Set<DeclarationMaladieProfessionnelle>();

    public DbSet<TransfertDossier> Transferts => Set<TransfertDossier>();

    public DbSet<PreuveDestruction> PreuvesDestruction => Set<PreuveDestruction>();

    public DbSet<ObligationDue> Obligations => Set<ObligationDue>();

    public DbSet<RendezVousPrevu> RendezVous => Set<RendezVousPrevu>();

    public DbSet<AffectationPersonne> Affectations => Set<AffectationPersonne>();

    public DbSet<ProfilRisquePoste> ProfilsRisques => Set<ProfilRisquePoste>();

    public DbSet<MesurageExposition> Mesurages => Set<MesurageExposition>();

    public DbSet<ParametreLegalLocal> Parametres => Set<ParametreLegalLocal>();

    public DbSet<CalendrierLocal> Calendriers => Set<CalendrierLocal>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        var encryptor = chiffrement.Encryptor;
        var texteChiffre = new EncryptedStringConverter(encryptor);

        ConfigurerDossier(modelBuilder, texteChiffre, encryptor);
        ConfigurerExamen(modelBuilder, texteChiffre, encryptor);
        ConfigurerDecision(modelBuilder, texteChiffre);
        ConfigurerProtocoles(modelBuilder);
        ConfigurerAutres(modelBuilder, encryptor);
        ConfigurerProjections(modelBuilder);
    }

    private static void ConfigurerDossier(ModelBuilder modelBuilder, ValueConverter<string, string> texteChiffre, FieldEncryptor encryptor)
    {
        modelBuilder.Entity<DossierSante>(b =>
        {
            b.ToTable("dossier_sante");
            b.HasKey(d => d.Id);
            b.Property(d => d.Id).ValueGeneratedNever();
            b.HasIndex(d => d.PersonneId).IsUnique();
            b.Property(d => d.GestionnaireCpmtId).HasMaxLength(UserLength);
            b.Property(d => d.StatutArchivage).HasConversion<string>().HasMaxLength(EnumLength);
            b.HasIndex(d => d.StatutArchivage);
            Enfants(b, d => d.Expositions, "dossier_id");
            Enfants(b, d => d.GroupesExposition, "dossier_id");
            Enfants(b, d => d.PiecesJointes, "dossier_id");
            Enfants(b, d => d.Questionnaires, "dossier_id");
            Enfants(b, d => d.Vaccinations, "dossier_id");
            Enfants(b, d => d.TestsTuberculiniques, "dossier_id");
        });

        modelBuilder.Entity<Exposition>(b =>
        {
            b.ToTable("exposition");
            Cle(b);
            b.Property(e => e.Agent).HasMaxLength(CodeLength);
            b.Property(e => e.Niveau).HasMaxLength(CodeLength);
            b.HasIndex("dossier_id", nameof(Exposition.MesurageId)).IsUnique().HasFilter("mesurage_id IS NOT NULL");
        });

        modelBuilder.Entity<RattachementGroupeExposition>(b =>
        {
            b.ToTable("rattachement_groupe_exposition");
            Cle(b);
            b.HasIndex(r => r.GroupeExpositionId);
            Validite(b.ComplexProperty(r => r.Periode));
        });

        modelBuilder.Entity<PieceJointe>(b =>
        {
            b.ToTable("piece_jointe");
            Cle(b);
            b.Property(p => p.Categorie).HasConversion<string>().HasMaxLength(EnumLength);
            Chiffre(b.Property(p => p.Titre), texteChiffre, "titre_chiffre");
            Chiffre(b.Property(p => p.Description), texteChiffre, "description_chiffre");
        });

        modelBuilder.Entity<QuestionnaireRempli>(b =>
        {
            b.ToTable("questionnaire_rempli");
            Cle(b);
            b.Property(q => q.ModeleCode).HasMaxLength(CodeLength);
            b.Property(q => q.Source).HasConversion<string>().HasMaxLength(EnumLength);
            JsonChiffre(b.Property(q => q.Reponses), encryptor, "reponses_chiffre");
        });

        modelBuilder.Entity<Vaccination>(b =>
        {
            b.ToTable("vaccination");
            Cle(b);
            b.Property(v => v.VaccinCode).HasMaxLength(CodeLength);
            b.Property(v => v.AdministrePar).HasMaxLength(UserLength);
            Chiffre(b.Property(v => v.Remarque), texteChiffre, "remarque_chiffre");
            b.HasOne<LotVaccin>().WithMany().HasForeignKey(v => v.LotId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TestTuberculinique>(b =>
        {
            b.ToTable("test_tuberculinique");
            Cle(b);
            b.Property(t => t.RealisePar).HasMaxLength(UserLength);
            JsonChiffre(b.Property(t => t.Lecture), encryptor, "lecture_chiffre");
        });
    }

    private static void ConfigurerExamen(ModelBuilder modelBuilder, ValueConverter<string, string> texteChiffre, FieldEncryptor encryptor)
    {
        modelBuilder.Entity<Examen>(b =>
        {
            b.ToTable("examen");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.HasOne<DossierSante>().WithMany().HasForeignKey(e => e.DossierId).IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(e => e.PersonneId);
            b.HasIndex(e => new { e.DossierId, e.ProfessionnelId });
            b.Property(e => e.TypeExamen).HasMaxLength(CodeLength);
            b.Property(e => e.ProfessionnelId).HasMaxLength(UserLength);
            b.Property(e => e.Statut).HasConversion<string>().HasMaxLength(EnumLength);
            b.PrimitiveCollection(e => e.ObligationIds).HasField("_obligationIds");
            b.HasOne(e => e.Observation).WithOne().HasForeignKey<ObservationClinique>("examen_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(e => e.Observation).AutoInclude();
            Enfants(b, e => e.Resultats, "examen_id");
            Enfants(b, e => e.PropositionsFrequence, "examen_id");
        });

        modelBuilder.Entity<ObservationClinique>(b =>
        {
            b.ToTable("observation_clinique");
            Cle(b);
            Chiffre(b.Property(o => o.Anamnese), texteChiffre, "anamnese_chiffre");
            Chiffre(b.Property(o => o.ExamenClinique), texteChiffre, "examen_clinique_chiffre");
        });

        modelBuilder.Entity<ResultatActe>(b =>
        {
            b.ToTable("resultat_acte");
            Cle(b);
            b.Property(r => r.TypeActe).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(r => r.Source).HasConversion<string>().HasMaxLength(EnumLength);
            JsonChiffre(b.Property(r => r.Mesures), encryptor, "valeurs_chiffre");
            Chiffre(b.Property(r => r.Commentaire), texteChiffre, "commentaire_chiffre");
        });

        modelBuilder.Entity<PropositionFrequence>(b =>
        {
            b.ToTable("proposition_frequence");
            Cle(b);
            b.Property(p => p.Statut).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(p => p.DecidePar).HasMaxLength(UserLength);
        });
    }

    private static void ConfigurerDecision(ModelBuilder modelBuilder, ValueConverter<string, string> texteChiffre)
    {
        modelBuilder.Entity<Decision>(b =>
        {
            b.ToTable("decision");
            b.HasKey(d => d.Id);
            b.Property(d => d.Id).ValueGeneratedNever();
            b.HasOne<Examen>().WithMany().HasForeignKey(d => d.ExamenId).IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(d => d.ExamenId).IsUnique();
            b.HasIndex(d => d.DossierId);
            b.HasIndex(d => d.PersonneId);
            b.Property(d => d.TypeExamen).HasMaxLength(CodeLength);
            b.Property(d => d.Categorie).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(d => d.Statut).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(d => d.AuteurId).HasMaxLength(UserLength);
            b.Property(d => d.ReferenceSignature).HasMaxLength(200);
            b.PrimitiveCollection(d => d.Mesures).HasField("_mesures");
            Chiffre(b.Property(d => d.Justification), texteChiffre, "justification_chiffre");
            Chiffre(b.Property(d => d.Recommandations), texteChiffre, "recommandations_chiffre");
            b.Ignore(d => d.DateRemise);
            Enfants(b, d => d.Recours, "decision_id");
        });

        modelBuilder.Entity<Recours>(b =>
        {
            b.ToTable("recours");
            Cle(b);
            b.Property(r => r.Type).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(r => r.Issue).HasConversion<string>().HasMaxLength(EnumLength);
            Chiffre(b.Property(r => r.Commentaire), texteChiffre, "commentaire_chiffre");
        });
    }

    private static void ConfigurerProtocoles(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ValeurReference>(b =>
        {
            b.ToTable("valeur_reference");
            Cle(b);
            b.Property(v => v.TypeActe).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(v => v.CodeMesure).HasMaxLength(CodeLength);
            b.Property(v => v.Unite).HasMaxLength(20);
            Libelle(b.ComplexProperty(v => v.Libelle), "libelle");
            Validite(b.ComplexProperty(v => v.Validite));
            b.HasIndex(v => new { v.TypeActe, v.CodeMesure });
        });

        modelBuilder.Entity<SchemaVaccinal>(b =>
        {
            b.ToTable("schema_vaccinal");
            Cle(b);
            b.Property(s => s.VaccinCode).HasMaxLength(CodeLength);
            Libelle(b.ComplexProperty(s => s.Libelle), "libelle");
            b.PrimitiveCollection(s => s.CodesRisques).HasField("_codesRisques");
            b.PrimitiveCollection(s => s.IntervallesMois).HasField("_intervallesMois");
        });

        modelBuilder.Entity<ModeleQuestionnaire>(b =>
        {
            b.ToTable("modele_questionnaire");
            Cle(b);
            b.Property(m => m.Code).HasMaxLength(CodeLength);
            b.HasIndex(m => new { m.Code, m.Version }).IsUnique();
            Libelle(b.ComplexProperty(m => m.Titre), "titre");
            JsonClair(b.Property(m => m.Questions), "questions");
        });

        modelBuilder.Entity<ModeleTexte>(b =>
        {
            b.ToTable("modele_texte");
            Cle(b);
            b.Property(m => m.CpmtId).HasMaxLength(UserLength);
            b.Property(m => m.Code).HasMaxLength(CodeLength);
            b.Property(m => m.Rubrique).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(m => m.Titre).HasMaxLength(200);
            b.Property(m => m.Texte).HasMaxLength(10_000);
            b.HasIndex(m => new { m.CpmtId, m.Code }).IsUnique().HasFilter("deleted_at IS NULL");
        });

        modelBuilder.Entity<DureeConservationExposition>(b =>
        {
            b.ToTable("duree_conservation_exposition");
            Cle(b);
            b.Property(d => d.CodeAgent).HasMaxLength(CodeLength);
            b.HasIndex(d => d.CodeAgent).IsUnique();
            b.Property(d => d.BaseLegale).HasMaxLength(300);
        });
    }

    private static void ConfigurerAutres(ModelBuilder modelBuilder, FieldEncryptor encryptor)
    {
        var texteChiffre = new EncryptedStringConverter(encryptor);

        modelBuilder.Entity<LotVaccin>(b =>
        {
            b.ToTable("lot_vaccin");
            Cle(b);
            b.Property(l => l.VaccinCode).HasMaxLength(CodeLength);
            b.Property(l => l.NumeroLot).HasMaxLength(50);
            b.HasIndex(l => new { l.CentreId, l.VaccinCode, l.NumeroLot }).IsUnique();
        });

        modelBuilder.Entity<DeclarationMaladieProfessionnelle>(b =>
        {
            b.ToTable("declaration_mp");
            Cle(b);
            b.HasOne<DossierSante>().WithMany().HasForeignKey(d => d.DossierId).IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Property(d => d.AuteurId).HasMaxLength(UserLength);
            b.Property(d => d.Statut).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(d => d.ReferenceFedris).HasMaxLength(100);
            JsonChiffre(b.Property(d => d.Contenu), encryptor, "contenu_chiffre");
            Enfants(b, d => d.DemandesInformation, "declaration_mp_id");
        });

        modelBuilder.Entity<DemandeInformationFedris>(b =>
        {
            b.ToTable("demande_information_fedris");
            Cle(b);
            Chiffre(b.Property(d => d.Objet), texteChiffre, "objet_chiffre");
            Chiffre(b.Property(d => d.Reponse), texteChiffre, "reponse_chiffre");
        });

        modelBuilder.Entity<TransfertDossier>(b =>
        {
            b.ToTable("transfert_dossier");
            Cle(b);
            b.HasOne<DossierSante>().WithMany().HasForeignKey(t => t.DossierId).IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Property(t => t.Direction).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(t => t.Motif).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(t => t.Statut).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(t => t.Contrepartie).HasMaxLength(CodeLength);
            b.Property(t => t.DemandePar).HasMaxLength(UserLength);
            b.Property(t => t.Empreinte).HasMaxLength(64);
            b.Property(t => t.ReferenceCanal).HasMaxLength(200);
            b.Property(t => t.Paquet).HasConversion((ValueConverter)texteChiffre).HasColumnName("paquet_chiffre");
        });

        modelBuilder.Entity<PreuveDestruction>(b =>
        {
            // Aucune clé étrangère : la preuve survit à la destruction du dossier (NF-22).
            b.ToTable("preuve_destruction");
            Cle(b);
            b.HasIndex(p => p.DossierId).IsUnique();
            b.Property(p => p.ValideePar).HasMaxLength(UserLength);
            b.Property(p => p.MotifValidation).HasMaxLength(300);
            b.Property(p => p.Empreinte).HasMaxLength(64);
        });
    }

    private static void ConfigurerProjections(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ObligationDue>(b =>
        {
            b.ToTable("obligation_due");
            b.HasKey(o => o.ObligationId);
            b.HasIndex(o => o.PersonneId);
            b.Property(o => o.TypeExamen).HasMaxLength(CodeLength);
            b.Property(o => o.StatutRetrait).HasMaxLength(EnumLength);
        });

        modelBuilder.Entity<RendezVousPrevu>(b =>
        {
            b.ToTable("rendez_vous_prevu");
            b.HasKey(r => r.RendezVousId);
            b.HasIndex(r => r.PersonneId);
            b.PrimitiveCollection(r => r.ObligationIds).HasField("_obligationIds");
        });

        modelBuilder.Entity<AffectationPersonne>(b =>
        {
            b.ToTable("affectation_personne");
            b.HasKey(a => a.AffectationId);
            b.HasIndex(a => a.PersonneId);
        });

        modelBuilder.Entity<ProfilRisquePoste>(b =>
        {
            b.ToTable("profil_risque_poste");
            b.HasKey(p => new { p.PosteId, p.ValideDu });
            b.PrimitiveCollection(p => p.CodesRisques).HasField("_codesRisques");
        });

        modelBuilder.Entity<MesurageExposition>(b =>
        {
            b.ToTable("mesurage_exposition");
            b.HasKey(m => m.MesurageId);
            b.HasIndex(m => m.GroupeExpositionId);
            b.Property(m => m.Agent).HasMaxLength(CodeLength);
            b.Property(m => m.Niveau).HasMaxLength(CodeLength);
        });

        modelBuilder.Entity<ParametreLegalLocal>(b =>
        {
            b.ToTable("parametre_legal_local");
            b.HasKey(p => new { p.Code, p.ValideDu });
            b.Property(p => p.Code).HasMaxLength(100);
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
    }

    private static void Cle<T>(EntityTypeBuilder<T> b)
        where T : Entity
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
    }

    private static void Enfants<TParent, TEnfant>(EntityTypeBuilder<TParent> b, System.Linq.Expressions.Expression<Func<TParent, IEnumerable<TEnfant>?>> navigation, string cle)
        where TParent : class
        where TEnfant : class
    {
        b.HasMany(navigation).WithOne().HasForeignKey(cle).IsRequired().OnDelete(DeleteBehavior.Cascade);
        b.Navigation(navigation!).AutoInclude();
    }

    /// <summary>Texte clinique chiffré : la colonne ne contient que <c>v1:&lt;clé&gt;:…</c> (ARC-45).</summary>
    private static void Chiffre<T>(PropertyBuilder<T> propriete, ValueConverter convertisseur, string colonne) =>
        propriete.HasConversion(convertisseur).HasColumnName(colonne).HasMaxLength(ChiffreLength);

    /// <summary>Contenu structuré sérialisé en JSON puis chiffré : la colonne ne contient que le texte chiffré (ARC-45).</summary>
    private static void JsonChiffre<T>(PropertyBuilder<T> propriete, FieldEncryptor encryptor, string colonne) =>
        propriete.HasConversion(
                new ValueConverter<T, string>(v => encryptor.Encrypt(JsonSerializer.Serialize(v, Json)), s => JsonSerializer.Deserialize<T>(encryptor.Decrypt(s), Json)!),
                Comparateur<T>())
            .HasColumnName(colonne)
            .HasMaxLength(ChiffreLength);

    /// <summary>Paramétrage sans donnée personnelle, stocké en jsonb.</summary>
    private static void JsonClair<T>(PropertyBuilder<T> propriete, string colonne) =>
        propriete.HasConversion(
                new ValueConverter<T, string>(v => JsonSerializer.Serialize(v, Json), s => JsonSerializer.Deserialize<T>(s, Json)!),
                Comparateur<T>())
            .HasColumnName(colonne)
            .HasColumnType("jsonb");

    private static ValueComparer<T> Comparateur<T>() => new(
        (a, b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json),
        v => JsonSerializer.Serialize(v, Json).GetHashCode(StringComparison.Ordinal),
        v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Json), Json)!);

    private static void Libelle(ComplexPropertyBuilder<LocalizedLabel> b, string prefix)
    {
        b.Property(l => l.Fr).HasColumnName($"{prefix}_fr").HasMaxLength(500);
        b.Property(l => l.Nl).HasColumnName($"{prefix}_nl").HasMaxLength(500);
        b.Property(l => l.De).HasColumnName($"{prefix}_de").HasMaxLength(500);
        b.Property(l => l.En).HasColumnName($"{prefix}_en").HasMaxLength(500);
    }

    private static void Validite(ComplexPropertyBuilder<Validity> b)
    {
        b.Property(v => v.ValidFrom).HasColumnName("valid_from");
        b.Property(v => v.ValidTo).HasColumnName("valid_to");
        b.Ignore(v => v.IsOpen);
    }
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
