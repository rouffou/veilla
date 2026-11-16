using System.Security.Cryptography;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Npgsql;

using Sepp.BuildingBlocks.Infrastructure.Security;

using Shouldly;

namespace Sepp.BuildingBlocks.Tests;

/// <summary>Contexte de test : NISS fictif chiffré et son index aveugle, sous index unique (comme <c>personne.niss_hash</c>).</summary>
public sealed class IndexAveugleTestDbContext(DbContextOptions<IndexAveugleTestDbContext> options, FieldEncryptor chiffreur) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<PersonneIndexee>(b =>
        {
            b.ToTable("personne_test");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).HasColumnName("id");
            b.Property(p => p.Niss).HasColumnName("niss_chiffre").HasConversion((ValueConverter)new EncryptedStringConverter(chiffreur));
            b.Property(p => p.NissHash).HasColumnName("niss_hash").HasMaxLength(64);
            b.HasIndex(p => p.NissHash).IsUnique();
        });
}

public sealed class PersonneIndexee
{
    public Guid Id { get; set; }

    public string Niss { get; set; } = string.Empty;

    public string NissHash { get; set; } = string.Empty;
}

/// <summary>Règles sans base : clés de l'index aveugle et description de l'index d'après le modèle.</summary>
public sealed class IndexAveugleTests
{
    [Fact]
    public void Les_empreintes_acceptees_sont_la_courante_puis_la_precedente()
    {
        var ancienne = RandomNumberGenerator.GetBytes(32);
        var nouvelle = RandomNumberGenerator.GetBytes(32);
        var avant = new BlindIndex(ancienne);
        var pendant = new BlindIndex(nouvelle, ancienne);

        avant.Empreintes("85.07.30-033.61").ShouldBe([avant.Compute("85073003361")]);
        avant.ComputePrecedente("85073003361").ShouldBeNull();
        pendant.Empreintes("85073003361").ShouldBe([new BlindIndex(nouvelle).Compute("85073003361"), avant.Compute("85073003361")]);
        pendant.AvecClePrecedente.ShouldBeTrue();
    }

    [Fact]
    public void Une_cle_precedente_identique_a_la_courante_est_refusee()
    {
        var cle = RandomNumberGenerator.GetBytes(32);

        Should.Throw<ArgumentException>(() => new BlindIndex(cle, [.. cle]));
    }

    [Fact]
    public void La_kcv_de_chaque_cle_vaut_les_8_premiers_caracteres_hexadecimaux_de_hmac_cle_kcv()
    {
        // Vecteurs calculés hors .NET : printf kcv | openssl dgst -sha256 -mac HMAC -macopt hexkey:<clé>.
        var zeros = new byte[32];
        var uns = Enumerable.Repeat((byte)1, 32).ToArray();

        new BlindIndex(zeros).Kcv.ShouldBe("43e1deb5");
        new BlindIndex(zeros).KcvPrecedente.ShouldBeNull();
        var pendant = new BlindIndex(uns, zeros);
        pendant.Kcv.ShouldBe("4ec061fe");
        pendant.KcvPrecedente.ShouldBe("43e1deb5");
    }

    [Fact]
    public void Le_journal_des_cles_donne_la_kcv_de_chaque_cle_et_jamais_la_cle()
    {
        var courante = RandomNumberGenerator.GetBytes(32);
        var precedente = RandomNumberGenerator.GetBytes(32);
        var index = new BlindIndex(courante, precedente);
        var journal = new JournalCapture();
        using var fabrique = LoggerFactory.Create(b => b.AddProvider(journal));

        JournalIndexAveugle.JournaliserCles(fabrique.CreateLogger(JournalIndexAveugle.Categorie), "personne.niss_hash", index);
        JournalIndexAveugle.JournaliserCles(fabrique.CreateLogger(JournalIndexAveugle.Categorie), "autre", new BlindIndex(courante));

        journal.Messages.ShouldBe(
        [
            $"Information: Index aveugle personne.niss_hash : clé courante, KCV {index.Kcv}. ",
            $"Information: Index aveugle personne.niss_hash : clé précédente acceptée en lecture, KCV {index.KcvPrecedente}. ",
            $"Information: Index aveugle autre : clé courante, KCV {index.Kcv}. ",
        ]);
        foreach (var cle in new[] { courante, precedente })
        {
            journal.Messages.ShouldAllBe(m => !m.Contains(Convert.ToHexString(cle), StringComparison.OrdinalIgnoreCase) && !m.Contains(Convert.ToBase64String(cle), StringComparison.Ordinal));
        }
    }

    [Fact]
    public void L_index_est_decrit_par_le_modele_et_sa_source_doit_etre_chiffree()
    {
        using var contexte = new IndexAveugleTestDbContext(
            new DbContextOptionsBuilder<IndexAveugleTestDbContext>().UseNpgsql("Host=localhost;Database=modele;Username=modele").Options, RechiffrementChampsTests.Chiffreur("d1"));

        ColonneIndexAveugle.Depuis(contexte.Model, typeof(PersonneIndexee), nameof(PersonneIndexee.Niss), nameof(PersonneIndexee.NissHash))
            .ShouldBe(new ColonneIndexAveugle(null, "personne_test", "niss_chiffre", "niss_hash", "id"));
        Should.Throw<InvalidOperationException>(() => ColonneIndexAveugle.Depuis(contexte.Model, typeof(PersonneIndexee), nameof(PersonneIndexee.NissHash), nameof(PersonneIndexee.NissHash)))
            .Message.ShouldContain("n'est pas une colonne chiffrée");
    }
}

/// <summary>
/// Recalcul de l'index aveugle sur un vrai PostgreSQL (#473), par le moteur de lots du rechiffrement : passage à la nouvelle
/// clé, rejeu, collision sur l'index unique, reprise après interruption, écriture concurrente, source illisible, modes de
/// l'hôte, et aucune donnée, empreinte ni clé dans le journal.
/// </summary>
public sealed class RecalculIndexAveuglePostgresTests(ServeurPostgresRechiffrement serveur) : IClassFixture<ServeurPostgresRechiffrement>
{
    private static readonly byte[] Ancienne = RandomNumberGenerator.GetBytes(32);
    private static readonly byte[] Nouvelle = RandomNumberGenerator.GetBytes(32);
    private static readonly BlindIndex Avant = new(Ancienne);
    private static readonly BlindIndex Pendant = new(Nouvelle, Ancienne);
    private static readonly OptionsRechiffrement Petit = new() { TailleLot = 3, PauseEntreLots = TimeSpan.Zero };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Niss(int i) => $"850730{i:D3}00";

    [Fact]
    public async Task Les_empreintes_passent_a_la_nouvelle_cle_le_rejeu_ne_change_rien_et_le_journal_ne_contient_rien_de_sensible()
    {
        var connexion = await CreerBaseAsync();
        var d1 = RechiffrementChampsTests.Chiffreur("d1");
        await using (var db = await OuvrirAsync(connexion))
        {
            for (var i = 0; i < 7; i++)
            {
                // Une ligne sur trois est déjà à jour (créée pendant la rotation).
                await InsererAsync(db, Guid.CreateVersion7(), d1.Encrypt(Niss(i)), i % 3 == 0 ? Pendant.Compute(Niss(i)) : Avant.Compute(Niss(i)));
            }
        }

        var journal = new JournalCapture();
        var rapport = await RecalculerAsync(connexion, d1, Pendant, Petit, journal);

        rapport.ShouldSatisfyAllConditions(
            r => r.Colonne.ShouldBe("personne_test.niss_hash"),
            r => r.Examinees.ShouldBe(7),
            r => r.Recalculees.ShouldBe(4),
            r => r.AJour.ShouldBe(3),
            r => r.Erreurs.ShouldBe(0),
            r => r.Verification.Courantes.ShouldBe(7),
            r => r.Complet.ShouldBeTrue());
        (await EmpreintesAsync(connexion)).Values.Order().ShouldBe(Enumerable.Range(0, 7).Select(i => Pendant.Compute(Niss(i))).Order());

        var avant = await EmpreintesAsync(connexion);
        var rejeu = await RecalculerAsync(connexion, d1, Pendant, Petit, journal);
        rejeu.Recalculees.ShouldBe(0);
        rejeu.AJour.ShouldBe(7);
        (await EmpreintesAsync(connexion)).ShouldBe(avant);

        var sensibles = Enumerable.Range(0, 7).SelectMany(i => new[] { Niss(i), Avant.Compute(Niss(i)), Pendant.Compute(Niss(i)) })
            .Concat(new[] { Ancienne, Nouvelle, RechiffrementChampsTests.D1 }.Select(Convert.ToBase64String)).ToList();
        journal.Messages.ShouldContain(m => m.Contains("Recalcul de l'index aveugle personne_test.niss_hash terminé", StringComparison.Ordinal));
        journal.Messages.ShouldAllBe(m => !sensibles.Any(s => m.Contains(s, StringComparison.Ordinal)) && !m.Contains("v2:d1:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Une_collision_sur_l_index_unique_est_comptee_en_erreur_sans_ecraser_ni_bloquer_les_autres()
    {
        var connexion = await CreerBaseAsync();
        var d1 = RechiffrementChampsTests.Chiffreur("d1");
        var collision = Guid.CreateVersion7();
        await using (var db = await OuvrirAsync(connexion))
        {
            await InsererAsync(db, Guid.CreateVersion7(), d1.Encrypt(Niss(1)), Avant.Compute(Niss(1)));
            await InsererAsync(db, collision, d1.Encrypt(Niss(2)), Avant.Compute(Niss(2)));
            // Doublon du NISS 2, déjà indexé avec la nouvelle clé : la ligne précédente ne peut pas prendre cette empreinte.
            await InsererAsync(db, Guid.CreateVersion7(), d1.Encrypt(Niss(2)), Pendant.Compute(Niss(2)));
            await InsererAsync(db, Guid.CreateVersion7(), d1.Encrypt(Niss(3)), Avant.Compute(Niss(3)));
        }

        var journal = new JournalCapture();
        var rapport = await RecalculerAsync(connexion, d1, Pendant, Petit, journal);

        rapport.ShouldSatisfyAllConditions(
            r => r.Recalculees.ShouldBe(2),
            r => r.AJour.ShouldBe(1),
            r => r.Collisions.ShouldBe(1),
            r => r.Erreurs.ShouldBe(1),
            r => r.Verification.ClePrecedente.ShouldBe(1),
            r => r.Complet.ShouldBeFalse());
        rapport.ErreursDetaillees.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Cle.ShouldBe(collision.ToString()),
            e => e.Message.ShouldContain("Collision"),
            e => e.Message.ShouldContain("23505"));
        (await EmpreintesAsync(connexion))[collision].ShouldBe(Avant.Compute(Niss(2)));
        journal.Messages.ShouldContain(m => m.Contains($"Empreinte non recalculée : personne_test.niss_hash, ligne {collision}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Une_interruption_entre_deux_lots_garde_les_lots_valides_et_la_relance_termine()
    {
        var connexion = await CreerBaseAsync();
        var d1 = RechiffrementChampsTests.Chiffreur("d1");
        await using (var db = await OuvrirAsync(connexion))
        {
            for (var i = 0; i < 7; i++)
            {
                await InsererAsync(db, Guid.CreateVersion7(), d1.Encrypt(Niss(i)), Avant.Compute(Niss(i)));
            }
        }

        using var annulation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await Should.ThrowAsync<OperationCanceledException>(() => RecalculerAsync(
            connexion, d1, Pendant, Petit with { PauseEntreLots = TimeSpan.FromMilliseconds(1) }, horloge: new HorlogeQuiAnnule(annulation), annulation: annulation.Token));

        (await VerifierAsync(connexion, d1, Pendant)).ShouldSatisfyAllConditions(
            v => v.Courantes.ShouldBe(3),
            v => v.ClePrecedente.ShouldBe(4),
            v => v.Complet.ShouldBeFalse());

        var reprise = await RecalculerAsync(connexion, d1, Pendant, Petit);
        reprise.Recalculees.ShouldBe(4);
        reprise.AJour.ShouldBe(3);
        reprise.Complet.ShouldBeTrue();
    }

    [Fact]
    public async Task Une_empreinte_modifiee_par_le_service_pendant_le_lot_n_est_pas_ecrasee()
    {
        var connexion = await CreerBaseAsync();
        var id = Guid.CreateVersion7();
        await using (var db = await OuvrirAsync(connexion))
        {
            await InsererAsync(db, id, RechiffrementChampsTests.Chiffreur("d1").Encrypt(Niss(1)), Avant.Compute(Niss(1)));
        }

        // Le service réécrit la ligne (NISS rechiffré, empreinte courante) entre la lecture et l'écriture du lot.
        var cles = RechiffrementChampsTests.Cles("d2");
        var ecriteParLeService = RechiffrementChampsTests.Chiffreur("d2").Encrypt(Niss(1));
        cles.AuPremierDechiffrement = () =>
        {
            using var autre = new NpgsqlConnection(connexion);
            autre.Open();
            using var maj = new NpgsqlCommand("UPDATE personne_test SET niss_chiffre = @n, niss_hash = @h WHERE id = @id", autre);
            maj.Parameters.AddWithValue("n", ecriteParLeService);
            maj.Parameters.AddWithValue("h", Pendant.Compute(Niss(1)));
            maj.Parameters.AddWithValue("id", id);
            maj.ExecuteNonQuery();
        };

        var rapport = await RecalculerAsync(connexion, new FieldEncryptor(cles), Pendant, Petit);

        rapport.Concurrentes.ShouldBe(1);
        rapport.Recalculees.ShouldBe(0);
        rapport.Complet.ShouldBeTrue();
        (await EmpreintesAsync(connexion))[id].ShouldBe(Pendant.Compute(Niss(1)));
    }

    [Fact]
    public async Task Une_source_illisible_est_comptee_sans_bloquer_les_autres_et_le_rapport_est_incomplet()
    {
        var connexion = await CreerBaseAsync();
        var d1 = RechiffrementChampsTests.Chiffreur("d1");
        var illisible = Guid.CreateVersion7();
        await using (var db = await OuvrirAsync(connexion))
        {
            await InsererAsync(db, Guid.CreateVersion7(), d1.Encrypt(Niss(1)), Avant.Compute(Niss(1)));
            await InsererAsync(db, illisible, "v2:zz:" + Convert.ToBase64String(new byte[40]), Avant.Compute(Niss(2)));
        }

        var rapport = await RecalculerAsync(connexion, d1, Pendant, Petit);

        rapport.Recalculees.ShouldBe(1);
        rapport.Erreurs.ShouldBe(1);
        rapport.ErreursDetaillees.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Cle.ShouldBe(illisible.ToString()),
            e => e.TypeErreur.ShouldBe(nameof(CryptographicException)));
        rapport.Verification.Illisibles.ShouldBe(1);
        rapport.Complet.ShouldBeFalse();
    }

    [Fact]
    public async Task Les_modes_de_l_hote_verifient_l_index_puis_le_recalculent_et_retournent_0_seulement_sans_ancienne_empreinte()
    {
        var connexion = await CreerBaseAsync();
        var d1 = RechiffrementChampsTests.Chiffreur("d1");
        await using (var db = await OuvrirAsync(connexion))
        {
            await InsererAsync(db, Guid.CreateVersion7(), d1.Encrypt(Niss(1)), Avant.Compute(Niss(1)));
            await InsererAsync(db, Guid.CreateVersion7(), d1.Encrypt(Niss(2)), Pendant.Compute(Niss(2)));
        }

        var avant = await EmpreintesAsync(connexion);
        var journal = new JournalCapture();

        // Vérification seule : une empreinte utilise encore l'ancienne clé, code 1 et rien n'est écrit (les champs, eux,
        // sont tous chiffrés avec la DEK courante).
        using (var verification = Hote(connexion, d1, Pendant, journal, verifierSeulement: true))
        {
            (await RechiffrerAsync(verification)).ShouldBe(RechiffrementHote.CodeIncomplet);
        }

        (await EmpreintesAsync(connexion)).ShouldBe(avant);
        journal.Messages.ShouldContain(m => m.Contains("Vérification de l'index aveugle personne_test.niss_hash : 2 ligne(s), 1 empreinte(s) de la clé courante, 1 de la clé précédente", StringComparison.Ordinal));
        journal.Messages.ShouldNotContain(m => m.Contains("Recalcul de l'index aveugle", StringComparison.Ordinal));

        // Rechiffrer puis quitter : l'index est recalculé, code 0 ; la vérification seule rend alors 0.
        using (var execution = Hote(connexion, d1, Pendant, journal))
        {
            (await RechiffrerAsync(execution)).ShouldBe(RechiffrementHote.CodeComplet);
        }

        using var apres = Hote(connexion, d1, new BlindIndex(Nouvelle), verifierSeulement: true);
        (await RechiffrerAsync(apres)).ShouldBe(RechiffrementHote.CodeComplet);
    }

    private static Task<int?> RechiffrerAsync(IHost hote) =>
        hote.RechiffrerChampsAsync<IndexAveugleTestDbContext>(
            s => s.GetRequiredService<FieldEncryptor>(),
            (s, modele) => [new IndexAveugleChiffre(Colonne(modele), s.GetRequiredService<BlindIndex>())],
            Ct);

    private static ColonneIndexAveugle Colonne(Microsoft.EntityFrameworkCore.Metadata.IModel modele) =>
        ColonneIndexAveugle.Depuis(modele, typeof(PersonneIndexee), nameof(PersonneIndexee.Niss), nameof(PersonneIndexee.NissHash));

    private static IHost Hote(string connexion, FieldEncryptor chiffreur, BlindIndex index, JournalCapture? journal = null, bool verifierSeulement = false)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [RechiffrementHote.CleExecuterPuisQuitter] = "true",
            [RechiffrementHote.CleVerifierSeulement] = verifierSeulement ? "true" : "false",
            ["Rechiffrement:PauseEntreLots"] = "00:00:00",
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(journal ?? new JournalCapture());
        builder.Services.AddSingleton(chiffreur);
        builder.Services.AddSingleton(index);
        builder.Services.AddDbContext<IndexAveugleTestDbContext>(o => o.UseNpgsql(connexion));
        return builder.Build();
    }

    private async Task<string> CreerBaseAsync()
    {
        var connexion = await serveur.CreerBaseAsync();
        await using var db = await OuvrirAsync(connexion);
        await using var creation = new NpgsqlCommand(
            "CREATE TABLE personne_test (id uuid PRIMARY KEY, niss_chiffre text NOT NULL, niss_hash character varying(64) NOT NULL); " +
            "CREATE UNIQUE INDEX ix_personne_test_niss_hash ON personne_test (niss_hash);", db);
        await creation.ExecuteNonQueryAsync(Ct);
        return connexion;
    }

    private static async Task<RapportRecalculIndexAveugle> RecalculerAsync(
        string connexion, FieldEncryptor chiffreur, BlindIndex index, OptionsRechiffrement options, JournalCapture? journal = null,
        TimeProvider? horloge = null, CancellationToken? annulation = null)
    {
        await using var db = await OuvrirAsync(connexion);
        using var fabrique = LoggerFactory.Create(b => b.AddProvider(journal ?? new JournalCapture()));
        return await RecalculIndexAveugle.ExecuterAsync(
            db, chiffreur, new IndexAveugleChiffre(new ColonneIndexAveugle(null, "personne_test", "niss_chiffre", "niss_hash", "id"), index), options,
            fabrique.CreateLogger("test"), horloge, annulation ?? Ct);
    }

    private static async Task<RapportVerificationIndexAveugle> VerifierAsync(string connexion, FieldEncryptor chiffreur, BlindIndex index)
    {
        await using var db = await OuvrirAsync(connexion);
        return await RecalculIndexAveugle.VerifierAsync(
            db, chiffreur, new IndexAveugleChiffre(new ColonneIndexAveugle(null, "personne_test", "niss_chiffre", "niss_hash", "id"), index),
            TimeSpan.FromSeconds(5), new JournalCapture().CreateLogger("test"), Ct);
    }

    private static async Task InsererAsync(NpgsqlConnection db, Guid id, string nissChiffre, string empreinte)
    {
        await using var commande = new NpgsqlCommand("INSERT INTO personne_test (id, niss_chiffre, niss_hash) VALUES (@id, @n, @h)", db);
        commande.Parameters.AddWithValue("id", id);
        commande.Parameters.AddWithValue("n", nissChiffre);
        commande.Parameters.AddWithValue("h", empreinte);
        await commande.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<Dictionary<Guid, string>> EmpreintesAsync(string connexion)
    {
        await using var db = await OuvrirAsync(connexion);
        await using var commande = new NpgsqlCommand("SELECT id, niss_hash FROM personne_test", db);
        await using var lecteur = await commande.ExecuteReaderAsync(Ct);
        var empreintes = new Dictionary<Guid, string>();
        while (await lecteur.ReadAsync(Ct))
        {
            empreintes[lecteur.GetGuid(0)] = lecteur.GetString(1);
        }

        return empreintes;
    }

    private static async Task<NpgsqlConnection> OuvrirAsync(string connexion)
    {
        var db = new NpgsqlConnection(connexion);
        await db.OpenAsync(Ct);
        return db;
    }

    /// <summary>Horloge qui annule le traitement dès qu'il demande une pause (interruption entre deux lots).</summary>
    private sealed class HorlogeQuiAnnule(CancellationTokenSource annulation) : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            annulation.Cancel();
            return System.CreateTimer(callback, state, dueTime, period);
        }
    }
}
