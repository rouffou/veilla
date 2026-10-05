using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.PostesRisques.Domain.Listes;
using Sepp.PostesRisques.Domain.Postes;
using Sepp.PostesRisques.Domain.Projections;
using Sepp.PostesRisques.Domain.Risques;
using Sepp.PostesRisques.Domain.Surcharges;

namespace Sepp.PostesRisques.Adapters.Persistence;

/// <summary>Base de données propre au service Postes et risques (ARC-02) : entités du §15.3 et modèles de lecture.</summary>
public sealed class PostesRisquesDbContext(DbContextOptions<PostesRisquesDbContext> options, ICurrentUser currentUser, TimeProvider timeProvider)
    : SeppDbContext(options, currentUser, timeProvider)
{
    private const int CodeLength = 50;
    private const int EnumLength = 40;
    private const int UserLength = 100;
    private const int TexteLength = 2000;

    public DbSet<Poste> Postes => Set<Poste>();

    public DbSet<PropositionPosteRisque> PropositionsPosteRisque => Set<PropositionPosteRisque>();

    public DbSet<Risque> Risques => Set<Risque>();

    public DbSet<SurchargeFrequence> Surcharges => Set<SurchargeFrequence>();

    public DbSet<ListeNominative> ListesNominatives => Set<ListeNominative>();

    public DbSet<PropositionListeNominative> PropositionsListe => Set<PropositionListeNominative>();

    public DbSet<AffectationPoste> Affectations => Set<AffectationPoste>();

    public DbSet<ExamenRealise> Examens => Set<ExamenRealise>();

    public DbSet<ParametreLegalLocal> Parametres => Set<ParametreLegalLocal>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        ConfigurerRisques(modelBuilder);
        ConfigurerPostes(modelBuilder);
        ConfigurerSurcharges(modelBuilder);
        ConfigurerListes(modelBuilder);
        ConfigurerProjections(modelBuilder);
    }

    private static void ConfigurerRisques(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Risque>(b =>
        {
            b.ToTable("risque");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).ValueGeneratedNever();
            b.Property(r => r.Code).HasMaxLength(CodeLength);
            b.HasIndex(r => r.Code).IsUnique();
            b.Property(r => r.Categorie).HasConversion<string>().HasMaxLength(EnumLength);
            Libelle(b.ComplexProperty(r => r.Libelle), "libelle");
            b.Property(r => r.ReferenceLegale).HasMaxLength(500);
            b.HasMany(r => r.Regles).WithOne().HasForeignKey("risque_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(r => r.Regles).AutoInclude();
            b.Ignore(r => r.RegleEnVigueur);
        });

        modelBuilder.Entity<RegleSurveillance>(b =>
        {
            b.ToTable("regle_surveillance");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).ValueGeneratedNever();
            b.Property(r => r.TypeSurveillance).HasConversion<string>().HasMaxLength(EnumLength);
            b.PrimitiveCollection(r => r.ActesSupplementaires);
            b.PrimitiveCollection(r => r.Vaccins);
            Validite(b.ComplexProperty(r => r.Validite));
            b.HasIndex("risque_id", nameof(RegleSurveillance.Version)).IsUnique();
        });
    }

    private static void ConfigurerPostes(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Poste>(b =>
        {
            b.ToTable("poste");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).ValueGeneratedNever();
            b.HasIndex(p => p.AffilieId);
            b.Property(p => p.Intitule).HasMaxLength(200);
            b.Property(p => p.Description).HasMaxLength(TexteLength);
            b.Property(p => p.MetierTypeCode).HasMaxLength(CodeLength);
            b.Property(p => p.Statut).HasConversion<string>().HasMaxLength(EnumLength);
            b.HasMany(p => p.Risques).WithOne().HasForeignKey("poste_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(p => p.Risques).AutoInclude();
        });

        modelBuilder.Entity<PosteRisque>(b =>
        {
            b.ToTable("poste_risque");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).ValueGeneratedNever();
            b.HasOne<Risque>().WithMany().HasForeignKey(r => r.RisqueId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.Property(r => r.RisqueCode).HasMaxLength(CodeLength);
            b.Property(r => r.NiveauExposition).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(r => r.ValideParCpmtId).HasMaxLength(UserLength);
            Validite(b.ComplexProperty(r => r.Validite));
            b.HasIndex("poste_id", nameof(PosteRisque.RisqueId));
        });

        modelBuilder.Entity<PropositionPosteRisque>(b =>
        {
            b.ToTable("proposition_poste_risque");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).ValueGeneratedNever();
            b.HasOne<Poste>().WithMany().HasForeignKey(p => p.PosteId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(p => new { p.AffilieId, p.Statut });
            b.Property(p => p.Origine).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(p => p.Statut).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(p => p.ProposeePar).HasMaxLength(UserLength);
            b.Property(p => p.DecidePar).HasMaxLength(UserLength);
            b.Property(p => p.Motif).HasMaxLength(TexteLength);
            b.Property(p => p.MotifRefus).HasMaxLength(TexteLength);
            b.HasMany(p => p.Lignes).WithOne().HasForeignKey("proposition_poste_risque_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(p => p.Lignes).AutoInclude();
        });

        modelBuilder.Entity<LignePropositionPosteRisque>(b =>
        {
            b.ToTable("ligne_proposition_poste_risque");
            b.HasKey(l => l.Id);
            b.Property(l => l.Id).ValueGeneratedNever();
            b.Property(l => l.Type).HasConversion<string>().HasMaxLength(EnumLength);
            b.HasOne<Risque>().WithMany().HasForeignKey(l => l.RisqueId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.Property(l => l.RisqueCode).HasMaxLength(CodeLength);
            b.Property(l => l.NiveauExposition).HasConversion<string>().HasMaxLength(EnumLength);
        });
    }

    private static void ConfigurerSurcharges(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<SurchargeFrequence>(b =>
        {
            b.ToTable("surcharge_frequence");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).ValueGeneratedNever();
            b.Property(s => s.CibleType).HasConversion<string>().HasMaxLength(EnumLength);
            b.HasIndex(s => new { s.AffilieId, s.CibleType, s.CibleId });
            b.HasOne<Risque>().WithMany().HasForeignKey(s => s.RisqueId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.Property(s => s.RisqueCode).HasMaxLength(CodeLength);
            b.Property(s => s.Motif).HasMaxLength(TexteLength);
            b.Property(s => s.CpmtId).HasMaxLength(UserLength);
            Validite(b.ComplexProperty(s => s.Validite));
        });

    private static void ConfigurerListes(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ListeNominative>(b =>
        {
            b.ToTable("liste_nominative");
            b.HasKey(l => l.Id);
            b.Property(l => l.Id).ValueGeneratedNever();
            b.Property(l => l.Type).HasConversion<string>().HasMaxLength(EnumLength);
            b.HasIndex(l => new { l.AffilieId, l.Type, l.Version }).IsUnique();
            b.Property(l => l.GenereePar).HasMaxLength(UserLength);
            b.HasMany(l => l.Lignes).WithOne().HasForeignKey("liste_nominative_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(l => l.Lignes).AutoInclude();
        });

        modelBuilder.Entity<LigneListeNominative>(b =>
        {
            b.ToTable("ligne_liste_nominative");
            b.HasKey(l => l.Id);
            b.Property(l => l.Id).ValueGeneratedNever();
            b.PrimitiveCollection(l => l.CodesRisques);
            b.Property(l => l.Origine).HasConversion<string>().HasMaxLength(EnumLength);
            b.HasIndex(l => l.PersonneId);
        });

        modelBuilder.Entity<PropositionListeNominative>(b =>
        {
            b.ToTable("proposition_liste_nominative");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).ValueGeneratedNever();
            b.HasOne<ListeNominative>().WithMany().HasForeignKey(p => p.ListeNominativeId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(p => new { p.AffilieId, p.Statut });
            b.Property(p => p.TypeListe).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(p => p.Origine).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(p => p.Statut).HasConversion<string>().HasMaxLength(EnumLength);
            b.Property(p => p.ProposeePar).HasMaxLength(UserLength);
            b.Property(p => p.DecidePar).HasMaxLength(UserLength);
            b.Property(p => p.Motif).HasMaxLength(TexteLength);
            b.Property(p => p.MotifRefus).HasMaxLength(TexteLength);
            b.HasMany(p => p.Lignes).WithOne().HasForeignKey("proposition_liste_nominative_id").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(p => p.Lignes).AutoInclude();
        });

        modelBuilder.Entity<LignePropositionListe>(b =>
        {
            b.ToTable("ligne_proposition_liste");
            b.HasKey(l => l.Id);
            b.Property(l => l.Id).ValueGeneratedNever();
            b.Property(l => l.Type).HasConversion<string>().HasMaxLength(EnumLength);
        });
    }

    private static void ConfigurerProjections(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AffectationPoste>(b =>
        {
            b.ToTable("affectation_poste");
            b.HasKey(a => a.AffectationId);
            b.Property(a => a.AffectationId).ValueGeneratedNever();
            b.HasIndex(a => a.PosteId);
            b.HasIndex(a => a.PersonneId);
        });

        modelBuilder.Entity<ExamenRealise>(b =>
        {
            b.ToTable("examen_realise");
            b.HasKey(e => e.ExamenId);
            b.Property(e => e.ExamenId).ValueGeneratedNever();
            b.Property(e => e.TypeExamen).HasMaxLength(100);
            b.HasIndex(e => new { e.AffilieId, e.PersonneId, e.Date });
        });

        modelBuilder.Entity<ParametreLegalLocal>(b =>
        {
            b.ToTable("parametre_legal_local");
            b.HasKey(p => new { p.Code, p.ValideDu });
            b.Property(p => p.Code).HasMaxLength(100);
            b.Property(p => p.Unite).HasMaxLength(EnumLength);
            b.Property(p => p.Valeur).HasPrecision(18, 4);
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
