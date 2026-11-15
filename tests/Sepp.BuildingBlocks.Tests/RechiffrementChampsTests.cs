using System.Security.Cryptography;
using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Npgsql;

using Sepp.BuildingBlocks.Infrastructure.Security;

using Shouldly;

using Testcontainers.PostgreSql;

namespace Sepp.BuildingBlocks.Tests;

/// <summary>Clés de test en mémoire : DEK v2 et anciennes clés v1, avec un rappel au premier usage d'une DEK.</summary>
public sealed class ClesDeTest(string courante, Dictionary<string, byte[]> deks, Dictionary<string, byte[]>? historiques = null) : IFieldKeyProvider
{
    public string CurrentKeyId { get; } = courante;

    /// <summary>Appelé une fois, au premier déchiffrement d'une valeur (simulation d'une écriture concurrente du service).</summary>
    public Action? AuPremierDechiffrement { get; set; }

    public byte[] GetKey(string keyId)
    {
        if (keyId != CurrentKeyId && Interlocked.Exchange(ref _premier, 1) == 0)
        {
            AuPremierDechiffrement?.Invoke();
        }

        return deks.TryGetValue(keyId, out var cle) ? cle : throw new CryptographicException($"Clé de données '{keyId}' inconnue.");
    }

    public byte[] GetLegacyKey(string keyId) =>
        historiques is not null && historiques.TryGetValue(keyId, out var cle) ? cle : throw new CryptographicException($"Clé de chiffrement historique '{keyId}' inconnue.");

    private int _premier;
}

/// <summary>Contexte de test : deux colonnes chiffrées (convention <c>_chiffre</c>/<c>_chiffree</c>) et une colonne en clair.</summary>
public sealed class RechiffrementTestDbContext(DbContextOptions<RechiffrementTestDbContext> options, FieldEncryptor chiffreur) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var converti = new EncryptedStringConverter(chiffreur);
        modelBuilder.Entity<FicheChiffree>(b =>
        {
            b.ToTable("fiche");
            b.HasKey(f => f.Id);
            b.Property(f => f.Id).HasColumnName("id");
            b.Property(f => f.Texte).HasColumnName("texte_chiffre").HasConversion((ValueConverter)converti);
            b.Property(f => f.Charge).HasColumnName("charge_chiffree")
                .HasConversion((ValueConverter)new ConvertisseurChiffre<string>(chiffreur, v => v, v => v));
            b.Property(f => f.Libelle).HasColumnName("libelle");
        });
    }
}

/// <summary>Contexte mal configuré : convertisseur de chiffrement marqué sur une colonne sans suffixe (#482).</summary>
public sealed class ColonneMarqueeSansSuffixeDbContext(DbContextOptions<ColonneMarqueeSansSuffixeDbContext> options, FieldEncryptor chiffreur) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<FicheChiffree>(b =>
        {
            b.ToTable("fiche");
            b.HasKey(f => f.Id);
            b.Property(f => f.Texte).HasColumnName("texte_chiffre").HasConversion((ValueConverter)new EncryptedStringConverter(chiffreur));
            b.Property(f => f.Libelle).HasColumnName("note").HasConversion((ValueConverter)new EncryptedStringConverter(chiffreur));
            b.Ignore(f => f.Charge);
        });
}

/// <summary>Contexte mal configuré : convertisseur écrit à la main, qui chiffre sans porter le marqueur (#482).</summary>
public sealed class ConvertisseurNonMarqueDbContext(DbContextOptions<ConvertisseurNonMarqueDbContext> options, FieldEncryptor chiffreur) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<FicheChiffree>(b =>
        {
            b.ToTable("fiche");
            b.HasKey(f => f.Id);
            b.Property(f => f.Texte).HasColumnName("texte_chiffre").HasConversion((ValueConverter)new EncryptedStringConverter(chiffreur));
            b.Property(f => f.Libelle).HasColumnName("note")
                .HasConversion(new ValueConverter<string, string>(v => chiffreur.Encrypt(v), v => chiffreur.Decrypt(v)));
            b.Ignore(f => f.Charge);
        });
}

public sealed class FicheChiffree
{
    public Guid Id { get; set; }

    public string Texte { get; set; } = string.Empty;

    public string? Charge { get; set; }

    public string Libelle { get; set; } = string.Empty;
}

/// <summary>Contexte mal configuré : une colonne nommée comme chiffrée mais sans convertisseur.</summary>
public sealed class ColonneSansConvertisseurDbContext(DbContextOptions<ColonneSansConvertisseurDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<FicheChiffree>(b =>
        {
            b.ToTable("fiche");
            b.HasKey(f => f.Id);
            b.Property(f => f.Texte).HasColumnName("texte_chiffre");
            b.Ignore(f => f.Charge);
        });
}

/// <summary>Contexte à clé composite : refusé par le rechiffrement (curseur sur une seule colonne).</summary>
public sealed class CleCompositeDbContext(DbContextOptions<CleCompositeDbContext> options, FieldEncryptor chiffreur) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<FicheChiffree>(b =>
        {
            b.ToTable("fiche");
            b.HasKey(f => new { f.Id, f.Libelle });
            b.Property(f => f.Texte).HasColumnName("texte_chiffre").HasConversion((ValueConverter)new EncryptedStringConverter(chiffreur));
            b.Ignore(f => f.Charge);
        });
}

/// <summary>Règles sans base : détection des colonnes chiffrées, rechiffrement d'une valeur, paramètres.</summary>
public sealed class RechiffrementChampsTests
{
    private const string Connexion = "Host=localhost;Database=modele;Username=modele";

    internal static readonly byte[] D1 = RandomNumberGenerator.GetBytes(32);
    internal static readonly byte[] D2 = RandomNumberGenerator.GetBytes(32);
    internal static readonly byte[] V1 = RandomNumberGenerator.GetBytes(32);

    internal static FieldEncryptor Chiffreur(string courante, bool avecV1 = true) => new(Cles(courante, avecV1));

    internal static ClesDeTest Cles(string courante, bool avecV1 = true) =>
        new(courante, new Dictionary<string, byte[]> { ["d1"] = D1, ["d2"] = D2 }, avecV1 ? new Dictionary<string, byte[]> { ["k1"] = V1 } : null);

    /// <summary>Valeur au format historique v1 (clé AES en clair, données associées = identifiant de clé).</summary>
    internal static string ChiffrerV1(string clair, string keyId = "k1")
    {
        var data = Encoding.UTF8.GetBytes(clair);
        var output = new byte[12 + data.Length + 16];
        RandomNumberGenerator.Fill(output.AsSpan(0, 12));
        using var aes = new AesGcm(V1, 16);
        aes.Encrypt(output.AsSpan(0, 12), data, output.AsSpan(12, data.Length), output.AsSpan(12 + data.Length), Encoding.UTF8.GetBytes(keyId));
        return $"v1:{keyId}:{Convert.ToBase64String(output)}";
    }

    [Fact]
    public void Les_colonnes_chiffrees_sont_celles_du_modele_suffixees_et_converties()
    {
        using var contexte = new RechiffrementTestDbContext(new DbContextOptionsBuilder<RechiffrementTestDbContext>().UseNpgsql(Connexion).Options, Chiffreur("d1"));

        ColonnesChiffrees.Depuis(contexte.Model).ShouldBe(
        [
            new ColonneChiffree(null, "fiche", "charge_chiffree", "id"),
            new ColonneChiffree(null, "fiche", "texte_chiffre", "id"),
        ]);
    }

    [Fact]
    public void Une_colonne_nommee_chiffree_sans_convertisseur_est_refusee()
    {
        using var contexte = new ColonneSansConvertisseurDbContext(new DbContextOptionsBuilder<ColonneSansConvertisseurDbContext>().UseNpgsql(Connexion).Options);

        Should.Throw<InvalidOperationException>(() => ColonnesChiffrees.Depuis(contexte.Model)).Message.ShouldContain("fiche.texte_chiffre");
    }

    [Fact]
    public void Une_colonne_marquee_chiffree_sans_suffixe_est_refusee()
    {
        using var contexte = new ColonneMarqueeSansSuffixeDbContext(new DbContextOptionsBuilder<ColonneMarqueeSansSuffixeDbContext>().UseNpgsql(Connexion).Options, Chiffreur("d1"));

        Should.Throw<InvalidOperationException>(() => ColonnesChiffrees.Depuis(contexte.Model)).Message.ShouldContain("fiche.note");
    }

    [Fact]
    public void Un_convertisseur_qui_chiffre_sans_le_marqueur_est_refuse_quel_que_soit_le_nom_de_colonne()
    {
        using var contexte = new ConvertisseurNonMarqueDbContext(new DbContextOptionsBuilder<ConvertisseurNonMarqueDbContext>().UseNpgsql(Connexion).Options, Chiffreur("d1"));

        Should.Throw<InvalidOperationException>(() => ColonnesChiffrees.Depuis(contexte.Model)).Message.ShouldContain(nameof(IConvertisseurChiffrement));
    }

    [Fact]
    public void Le_convertisseur_chiffre_generique_chiffre_le_texte_de_la_valeur_et_la_relit()
    {
        var chiffreur = Chiffreur("d1");
        var convertisseur = new ConvertisseurChiffre<DayOfWeek>(chiffreur, j => j.ToString(), Enum.Parse<DayOfWeek>);

        var stocke = (string)convertisseur.ConvertToProvider(DayOfWeek.Friday)!;

        stocke.ShouldStartWith("v2:d1:");
        chiffreur.Decrypt(stocke).ShouldBe("Friday");
        convertisseur.ConvertFromProvider(stocke).ShouldBe(DayOfWeek.Friday);
        convertisseur.ShouldBeAssignableTo<IConvertisseurChiffrement>();
    }

    [Fact]
    public void Une_table_a_cle_composite_est_refusee()
    {
        using var contexte = new CleCompositeDbContext(new DbContextOptionsBuilder<CleCompositeDbContext>().UseNpgsql(Connexion).Options, Chiffreur("d1"));

        Should.Throw<InvalidOperationException>(() => ColonnesChiffrees.Depuis(contexte.Model)).Message.ShouldContain("clé primaire à une seule colonne");
    }

    [Fact]
    public void Rechiffrer_passe_v1_et_l_ancienne_DEK_a_la_DEK_courante_et_laisse_une_valeur_a_jour_intacte()
    {
        var ancien = Chiffreur("d1");
        var courant = Chiffreur("d2");
        var aJour = courant.Encrypt("déjà à jour");

        var depuisV1 = courant.Rechiffrer(ChiffrerV1("valeur v1"));
        var depuisD1 = courant.Rechiffrer(ancien.Encrypt("valeur d1"));

        depuisV1.ShouldStartWith("v2:d2:");
        courant.Decrypt(depuisV1).ShouldBe("valeur v1");
        depuisD1.ShouldStartWith("v2:d2:");
        courant.Decrypt(depuisD1).ShouldBe("valeur d1");
        courant.Rechiffrer(aJour).ShouldBeSameAs(aJour);
        courant.PrefixeCourant.ShouldBe("v2:d2:");
    }

    [Fact]
    public void Rechiffrer_une_valeur_v1_sans_ancienne_cle_configuree_echoue()
    {
        var valeur = ChiffrerV1("valeur v1");

        Should.Throw<CryptographicException>(() => Chiffreur("d2", avecV1: false).Rechiffrer(valeur));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("10001")]
    public void Une_taille_de_lot_hors_bornes_est_refusee(string taille) =>
        Should.Throw<InvalidOperationException>(() => RechiffrementHote.Options(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Rechiffrement:TailleLot"] = taille }).Build()));

    [Fact]
    public void Les_parametres_ont_des_valeurs_par_defaut_et_se_configurent()
    {
        RechiffrementHote.Options(new ConfigurationBuilder().Build()).ShouldBe(new OptionsRechiffrement());

        var options = RechiffrementHote.Options(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Rechiffrement:TailleLot"] = "25",
            ["Rechiffrement:PauseEntreLots"] = "00:00:01",
        }).Build());

        options.TailleLot.ShouldBe(25);
        options.PauseEntreLots.ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Sans_le_mode_demande_le_service_sert_sans_ouvrir_la_base()
    {
        using var hote = Host.CreateApplicationBuilder().Build();

        (await hote.RechiffrerChampsAsync<RechiffrementTestDbContext>(_ => throw new InvalidOperationException("ne doit pas être appelé"), TestContext.Current.CancellationToken))
            .ShouldBeNull();
    }
}

/// <summary>Serveur PostgreSQL éphémère partagé par les tests de rechiffrement.</summary>
public sealed class ServeurPostgresRechiffrement : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string Connexion(string baseDeDonnees) => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = baseDeDonnees }.ConnectionString;

    public async Task<string> CreerBaseAsync()
    {
        var nom = "rech_" + Guid.NewGuid().ToString("N")[..12];
        await using var connexion = new NpgsqlConnection(_postgres.GetConnectionString());
        await connexion.OpenAsync();
        await using (var commande = new NpgsqlCommand($"CREATE DATABASE {nom}", connexion))
        {
            await commande.ExecuteNonQueryAsync();
        }

        await using var contexte = Contexte(Connexion(nom), RechiffrementChampsTests.Chiffreur("d1"));
        await contexte.Database.EnsureCreatedAsync();
        return Connexion(nom);
    }

    public static RechiffrementTestDbContext Contexte(string connexion, FieldEncryptor chiffreur) =>
        new(new DbContextOptionsBuilder<RechiffrementTestDbContext>().UseNpgsql(connexion).Options, chiffreur);

    public async ValueTask InitializeAsync() => await _postgres.StartAsync();

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();
}

/// <summary>
/// Rechiffrement sur un vrai PostgreSQL (#396) : données v1 et v2 mélangées, rotation de DEK, rejeu idempotent, reprise
/// après interruption, écriture concurrente, valeur illisible, et aucune donnée ni clé dans le journal.
/// </summary>
public sealed class RechiffrementChampsPostgresTests(ServeurPostgresRechiffrement serveur) : IClassFixture<ServeurPostgresRechiffrement>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly OptionsRechiffrement Petit = new() { TailleLot = 3, PauseEntreLots = TimeSpan.Zero };

    [Fact]
    public async Task Les_valeurs_v1_et_d1_passent_a_d2_sont_relues_et_le_rejeu_ne_change_rien()
    {
        var connexion = await serveur.CreerBaseAsync();
        var d1 = RechiffrementChampsTests.Chiffreur("d1");
        var d2 = RechiffrementChampsTests.Chiffreur("d2");
        var attendues = new Dictionary<Guid, (string Texte, string? Charge)>();
        await using (var db = await OuvrirAsync(connexion))
        {
            for (var i = 0; i < 10; i++)
            {
                var texte = $"texte clinique fictif {i}";
                var charge = i % 3 == 0 ? null : $"charge fictive {i}";
                var stockeTexte = (i % 3) switch { 0 => RechiffrementChampsTests.ChiffrerV1(texte), 1 => d1.Encrypt(texte), _ => d2.Encrypt(texte) };
                var stockeCharge = charge is null ? null : i % 2 == 0 ? RechiffrementChampsTests.ChiffrerV1(charge) : d1.Encrypt(charge);
                var id = Guid.CreateVersion7();
                await InsererAsync(db, id, stockeTexte, stockeCharge);
                attendues[id] = (texte, charge);
            }
        }

        var journal = new JournalCapture();
        var rapport = await ExecuterAsync(connexion, d2, Petit, journal);

        rapport.Complet.ShouldBeTrue();
        rapport.RestantesV1.ShouldBe(0);
        Colonne(rapport, "texte_chiffre").Rechiffrees.ShouldBe(7);   // 4 v1 + 3 d1 ; 3 déjà en d2
        Colonne(rapport, "texte_chiffre").Courantes.ShouldBe(10);
        Colonne(rapport, "charge_chiffree").Rechiffrees.ShouldBe(6);   // 4 lignes sans charge
        Colonne(rapport, "charge_chiffree").Courantes.ShouldBe(6);

        // Relecture avec la seule DEK courante (aucune ancienne clé) : chaque valeur retrouve son clair.
        var seulementD2 = new FieldEncryptor(new ClesDeTest("d2", new Dictionary<string, byte[]> { ["d2"] = RechiffrementChampsTests.D2 }));
        await using (var db = await OuvrirAsync(connexion))
        await using (var lecture = new NpgsqlCommand("SELECT id, texte_chiffre, charge_chiffree FROM fiche", db))
        await using (var lecteur = await lecture.ExecuteReaderAsync(Ct))
        {
            var relues = 0;
            while (await lecteur.ReadAsync(Ct))
            {
                var (texte, charge) = attendues[lecteur.GetGuid(0)];
                seulementD2.Decrypt(lecteur.GetString(1)).ShouldBe(texte);
                (lecteur.IsDBNull(2) ? null : seulementD2.Decrypt(lecteur.GetString(2))).ShouldBe(charge);
                relues++;
            }

            relues.ShouldBe(10);
        }

        (await ValeursAsync(connexion)).ShouldAllBe(v => v == null || v.StartsWith("v2:d2:", StringComparison.Ordinal));

        // Rejeu : rien à lire, rien à écrire.
        var avant = await ValeursAsync(connexion);
        var rejeu = await ExecuterAsync(connexion, d2, Petit, journal);
        rejeu.Complet.ShouldBeTrue();
        rejeu.Rechiffrees.ShouldBe(0);
        rejeu.Colonnes.ShouldAllBe(c => c.Examinees == 0);
        (await ValeursAsync(connexion)).ShouldBe(avant);

        // Ni clair, ni valeur chiffrée, ni clé dans le journal.
        var clesB64 = new[] { RechiffrementChampsTests.D1, RechiffrementChampsTests.D2, RechiffrementChampsTests.V1 }.Select(Convert.ToBase64String).ToList();
        journal.Messages.ShouldNotBeEmpty();
        journal.Messages.ShouldAllBe(m => !m.Contains("fictive", StringComparison.Ordinal) && !m.Contains("fictif", StringComparison.Ordinal)
                                          && !m.Contains("v2:d1:", StringComparison.Ordinal) && !m.Contains("v1:k1:", StringComparison.Ordinal)
                                          && !clesB64.Any(c => m.Contains(c, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task La_rotation_de_d2_vers_d1_puis_retour_est_symetrique()
    {
        var connexion = await serveur.CreerBaseAsync();
        await using (var db = await OuvrirAsync(connexion))
        {
            for (var i = 0; i < 4; i++)
            {
                await InsererAsync(db, Guid.CreateVersion7(), RechiffrementChampsTests.Chiffreur("d1").Encrypt($"t{i}"), null);
            }
        }

        (await ExecuterAsync(connexion, RechiffrementChampsTests.Chiffreur("d2"), Petit)).Rechiffrees.ShouldBe(4);
        (await ExecuterAsync(connexion, RechiffrementChampsTests.Chiffreur("d1"), Petit)).Rechiffrees.ShouldBe(4);
        (await ValeursAsync(connexion)).Where(v => v is not null).ShouldAllBe(v => v!.StartsWith("v2:d1:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Une_valeur_illisible_est_comptee_sans_bloquer_les_autres_et_le_rapport_est_incomplet()
    {
        var connexion = await serveur.CreerBaseAsync();
        var d1 = RechiffrementChampsTests.Chiffreur("d1");
        var inconnue = Guid.CreateVersion7();
        await using (var db = await OuvrirAsync(connexion))
        {
            await InsererAsync(db, Guid.CreateVersion7(), d1.Encrypt("a"), null);
            await InsererAsync(db, inconnue, "v2:zz:" + Convert.ToBase64String(new byte[40]), null);
            await InsererAsync(db, Guid.CreateVersion7(), RechiffrementChampsTests.ChiffrerV1("b"), null);
            await InsererAsync(db, Guid.CreateVersion7(), "v2:d1:%%%pas-du-base64%%%", null);
        }

        var journal = new JournalCapture();
        var rapport = await ExecuterAsync(connexion, RechiffrementChampsTests.Chiffreur("d2"), new OptionsRechiffrement { TailleLot = 1, PauseEntreLots = TimeSpan.Zero }, journal);

        rapport.Complet.ShouldBeFalse();
        rapport.Rechiffrees.ShouldBe(2);
        rapport.Erreurs.ShouldBe(2);
        rapport.Restantes.ShouldBe(2);
        rapport.ErreursDetaillees.ShouldContain(e => e.Cle == inconnue.ToString() && e.Colonne == "fiche.texte_chiffre" && e.TypeErreur == nameof(CryptographicException));
        rapport.ErreursDetaillees.ShouldContain(e => e.TypeErreur == nameof(FormatException));
        journal.Messages.ShouldContain(m => m.Contains("Valeur non rechiffrée : fiche.texte_chiffre", StringComparison.Ordinal));
        journal.Messages.ShouldAllBe(m => !m.Contains("pas-du-base64", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Une_interruption_entre_deux_lots_garde_les_lots_valides_et_la_relance_termine()
    {
        var connexion = await serveur.CreerBaseAsync();
        await using (var db = await OuvrirAsync(connexion))
        {
            for (var i = 0; i < 7; i++)
            {
                await InsererAsync(db, Guid.CreateVersion7(), RechiffrementChampsTests.ChiffrerV1($"t{i}"), null);
            }
        }

        // Annulation au moment de la pause qui suit le premier lot validé (3 valeurs).
        using var annulation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var interrompu = false;
        try
        {
            await ExecuterAsync(
                connexion, RechiffrementChampsTests.Chiffreur("d2"), new OptionsRechiffrement { TailleLot = 3, PauseEntreLots = TimeSpan.FromMilliseconds(1) },
                horloge: new HorlogeQuiAnnule(annulation), annulation: annulation.Token);
        }
        catch (OperationCanceledException)
        {
            interrompu = true;
        }

        interrompu.ShouldBeTrue();

        (await ValeursAsync(connexion)).Count(v => v is not null && v.StartsWith("v2:d2:", StringComparison.Ordinal)).ShouldBe(3);

        var reprise = await ExecuterAsync(connexion, RechiffrementChampsTests.Chiffreur("d2"), Petit);
        reprise.Complet.ShouldBeTrue();
        Colonne(reprise, "texte_chiffre").Rechiffrees.ShouldBe(4);
    }

    [Fact]
    public async Task Une_valeur_modifiee_par_le_service_pendant_le_lot_n_est_pas_ecrasee()
    {
        var connexion = await serveur.CreerBaseAsync();
        var id = Guid.CreateVersion7();
        await using (var db = await OuvrirAsync(connexion))
        {
            await InsererAsync(db, id, RechiffrementChampsTests.Chiffreur("d1").Encrypt("ancienne"), null);
        }

        var d2 = RechiffrementChampsTests.Chiffreur("d2");
        var ecriteParLeService = d2.Encrypt("nouvelle, saisie pendant le rechiffrement");
        var cles = RechiffrementChampsTests.Cles("d2");
        cles.AuPremierDechiffrement = () =>
        {
            using var autre = new NpgsqlConnection(connexion);
            autre.Open();
            using var maj = new NpgsqlCommand("UPDATE fiche SET texte_chiffre = @v WHERE id = @id", autre);
            maj.Parameters.AddWithValue("v", ecriteParLeService);
            maj.Parameters.AddWithValue("id", id);
            maj.ExecuteNonQuery();
        };

        var rapport = await ExecuterAsync(connexion, new FieldEncryptor(cles), Petit);

        Colonne(rapport, "texte_chiffre").Concurrentes.ShouldBe(1);
        Colonne(rapport, "texte_chiffre").Rechiffrees.ShouldBe(0);
        rapport.Complet.ShouldBeTrue();
        (await ValeursAsync(connexion)).ShouldContain(ecriteParLeService);
    }

    [Fact]
    public async Task Le_mode_d_hote_rechiffre_puis_retourne_0_et_1_si_des_valeurs_restent()
    {
        var connexion = await serveur.CreerBaseAsync();
        await using (var db = await OuvrirAsync(connexion))
        {
            await InsererAsync(db, Guid.CreateVersion7(), RechiffrementChampsTests.ChiffrerV1("x"), null);
        }

        // Sans l'ancienne clé v1 : la valeur reste, code 1.
        using (var sansV1 = Hote(connexion, RechiffrementChampsTests.Chiffreur("d2", avecV1: false)))
        {
            (await sansV1.RechiffrerChampsAsync<RechiffrementTestDbContext>(s => s.GetRequiredService<FieldEncryptor>(), Ct)).ShouldBe(RechiffrementHote.CodeIncomplet);
        }

        using var hote = Hote(connexion, RechiffrementChampsTests.Chiffreur("d2"));
        (await hote.RechiffrerChampsAsync<RechiffrementTestDbContext>(s => s.GetRequiredService<FieldEncryptor>(), Ct)).ShouldBe(RechiffrementHote.CodeComplet);
    }

    [Fact]
    public async Task Un_echec_de_connexion_retourne_1_sans_journaliser_le_mot_de_passe()
    {
        const string motDePasse = "mot-de-passe-de-test-a-ne-pas-journaliser";
        var journal = new JournalCapture();
        var connexion = new NpgsqlConnectionStringBuilder { Host = "127.0.0.1", Port = 1, Database = "absente", Username = "u", Password = motDePasse, Timeout = 3 }.ConnectionString;
        using var hote = Hote(connexion, RechiffrementChampsTests.Chiffreur("d2"), journal);

        (await hote.RechiffrerChampsAsync<RechiffrementTestDbContext>(s => s.GetRequiredService<FieldEncryptor>(), Ct)).ShouldBe(RechiffrementHote.CodeIncomplet);
        journal.Messages.ShouldContain(m => m.Contains("Échec du rechiffrement des champs de RechiffrementTestDbContext", StringComparison.Ordinal));
        journal.Messages.ShouldAllBe(m => !m.Contains(motDePasse, StringComparison.Ordinal));
    }

    [Fact]
    public async Task La_requete_de_controle_documentee_compte_les_valeurs_v1_et_hors_DEK_courante()
    {
        var connexion = await serveur.CreerBaseAsync();
        var d1 = RechiffrementChampsTests.Chiffreur("d1");
        var d2 = RechiffrementChampsTests.Chiffreur("d2");
        await using (var db = await OuvrirAsync(connexion))
        {
            await InsererAsync(db, Guid.CreateVersion7(), RechiffrementChampsTests.ChiffrerV1("a"), d1.Encrypt("b"));
            await InsererAsync(db, Guid.CreateVersion7(), d2.Encrypt("c"), RechiffrementChampsTests.ChiffrerV1("d"));
            await InsererAsync(db, Guid.CreateVersion7(), d1.Encrypt("e"), null);
        }

        // Requête de infra/README.md, telle qu'écrite, avec la DEK courante du test à la place de D2.
        var controle = RequeteDeControle().Replace("(?!D2:)", "(?!d2:)", StringComparison.Ordinal);
        controle.ShouldNotContain("D2:", Case.Sensitive);

        (await ControlerAsync(connexion, controle)).ShouldBe(
        [
            ("fiche", "charge_chiffree", 2L, 1L, 2L),
            ("fiche", "texte_chiffre", 3L, 1L, 2L),
        ]);

        await ExecuterAsync(connexion, d2, Petit);
        (await ControlerAsync(connexion, controle)).ShouldAllBe(l => l.V1 == 0 && l.HorsCourante == 0);
    }

    [Fact]
    public async Task La_requete_de_controle_documentee_trouve_une_colonne_chiffree_sans_suffixe_et_un_contenu_json()
    {
        var connexion = await serveur.CreerBaseAsync();
        var d1 = RechiffrementChampsTests.Chiffreur("d1");
        var d2 = RechiffrementChampsTests.Chiffreur("d2");
        await using (var db = await OuvrirAsync(connexion))
        {
            await AjouterColonneJsonAsync(db);
            await InsererAsync(db, Guid.CreateVersion7(), d2.Encrypt("a"), null, libelle: d1.Encrypt("note sans suffixe"));
            var id = Guid.CreateVersion7();
            await InsererAsync(db, id, d2.Encrypt("b"), null);
            await EcrireJsonAsync(db, id, RechiffrementChampsTests.ChiffrerV1("réponse"));
        }

        var controle = RequeteDeControle().Replace("(?!D2:)", "(?!d2:)", StringComparison.Ordinal);

        (await ControlerAsync(connexion, controle)).ShouldBe(
        [
            ("fiche", "donnees", 1L, 1L, 1L),
            ("fiche", "libelle", 1L, 0L, 1L),
            ("fiche", "texte_chiffre", 2L, 0L, 0L),
        ]);
    }

    [Fact]
    public async Task Une_valeur_chiffree_hors_du_modele_rend_le_rapport_incomplet_meme_avec_la_DEK_courante()
    {
        var connexion = await serveur.CreerBaseAsync();
        var d2 = RechiffrementChampsTests.Chiffreur("d2");
        await using (var db = await OuvrirAsync(connexion))
        {
            await AjouterColonneJsonAsync(db);
            await InsererAsync(db, Guid.CreateVersion7(), RechiffrementChampsTests.ChiffrerV1("a"), null, libelle: d2.Encrypt("note sans suffixe"));
            var id = Guid.CreateVersion7();
            await InsererAsync(db, id, d2.Encrypt("b"), null);
            await EcrireJsonAsync(db, id, RechiffrementChampsTests.ChiffrerV1("réponse"));
        }

        var journal = new JournalCapture();
        var rapport = await ExecuterAsync(connexion, d2, Petit, journal);

        rapport.Erreurs.ShouldBe(0);
        Colonne(rapport, "texte_chiffre").Rechiffrees.ShouldBe(1);
        rapport.Complet.ShouldBeFalse();
        rapport.Verification.ColonnesHorsModele.Select(c => (c.Colonne, c.Chiffrees, c.HorsDekCourante, c.V1)).ShouldBe(
        [
            ("fiche.donnees", 1L, 1L, 1L),
            ("fiche.libelle", 1L, 0L, 0L),
        ]);
        journal.Messages.ShouldContain(m => m.Contains("Colonne fiche.libelle", StringComparison.Ordinal) && m.Contains("ne la déclare pas chiffrée", StringComparison.Ordinal));
        journal.Messages.ShouldAllBe(m => !m.Contains("note sans suffixe", StringComparison.Ordinal) && !m.Contains("v1:k1:", StringComparison.Ordinal));

        using var hote = Hote(connexion, d2, verifierSeulement: true);
        (await hote.RechiffrerChampsAsync<RechiffrementTestDbContext>(s => s.GetRequiredService<FieldEncryptor>(), Ct)).ShouldBe(RechiffrementHote.CodeIncomplet);
    }

    [Fact]
    public async Task Le_mode_verification_seule_n_ecrit_rien_l_emporte_et_retourne_1_puis_0_apres_rechiffrement()
    {
        var connexion = await serveur.CreerBaseAsync();
        await using (var db = await OuvrirAsync(connexion))
        {
            await InsererAsync(db, Guid.CreateVersion7(), RechiffrementChampsTests.ChiffrerV1("x"), RechiffrementChampsTests.Chiffreur("d1").Encrypt("y"));
        }

        var avant = await ValeursAsync(connexion);
        var journal = new JournalCapture();

        // Les deux modes demandés : la vérification l'emporte, rien n'est écrit.
        using (var verification = Hote(connexion, RechiffrementChampsTests.Chiffreur("d2"), journal, verifierSeulement: true))
        {
            (await verification.RechiffrerChampsAsync<RechiffrementTestDbContext>(s => s.GetRequiredService<FieldEncryptor>(), Ct)).ShouldBe(RechiffrementHote.CodeIncomplet);
        }

        (await ValeursAsync(connexion)).ShouldBe(avant);
        journal.Messages.ShouldContain(m => m.Contains("Vérification du chiffrement (v2:d2:)", StringComparison.Ordinal) && m.Contains("2 valeur(s) hors DEK courante dont 1 au format v1", StringComparison.Ordinal));
        journal.Messages.ShouldNotContain(m => m.Contains("Rechiffrement des champs", StringComparison.Ordinal));

        (await ExecuterAsync(connexion, RechiffrementChampsTests.Chiffreur("d2"), Petit)).Complet.ShouldBeTrue();

        using var apres = Hote(connexion, RechiffrementChampsTests.Chiffreur("d2"), verifierSeulement: true);
        (await apres.RechiffrerChampsAsync<RechiffrementTestDbContext>(s => s.GetRequiredService<FieldEncryptor>(), Ct)).ShouldBe(RechiffrementHote.CodeComplet);
    }

    [Fact]
    public async Task Une_ligne_verrouillee_par_une_transaction_ouverte_reste_en_erreur_apres_le_delai_sans_bloquer_les_autres()
    {
        var connexion = await serveur.CreerBaseAsync();
        var d1 = RechiffrementChampsTests.Chiffreur("d1");
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.CreateVersion7()).ToList();
        await using (var db = await OuvrirAsync(connexion))
        {
            foreach (var id in ids)
            {
                await InsererAsync(db, id, d1.Encrypt("t"), null);
            }
        }

        // Une transaction du service, restée ouverte, verrouille la deuxième ligne.
        await using var service = await OuvrirAsync(connexion);
        await using var transaction = await service.BeginTransactionAsync(Ct);
        await using (var verrou = new NpgsqlCommand("SELECT 1 FROM fiche WHERE id = @id FOR UPDATE", service, transaction))
        {
            verrou.Parameters.AddWithValue("id", ids[1]);
            await verrou.ExecuteScalarAsync(Ct);
        }

        var chrono = System.Diagnostics.Stopwatch.StartNew();
        var rapport = await ExecuterAsync(connexion, RechiffrementChampsTests.Chiffreur("d2"), Petit with { DelaiVerrou = TimeSpan.FromMilliseconds(200) });
        chrono.Stop();

        chrono.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
        Colonne(rapport, "texte_chiffre").Rechiffrees.ShouldBe(3);
        Colonne(rapport, "texte_chiffre").Erreurs.ShouldBe(1);
        rapport.ErreursDetaillees.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Cle.ShouldBe(ids[1].ToString()),
            e => e.Message.ShouldContain("55P03"));
        rapport.Restantes.ShouldBe(1);
        rapport.Complet.ShouldBeFalse();

        // Le service libère la ligne : la relance termine.
        await transaction.RollbackAsync(Ct);
        var relance = await ExecuterAsync(connexion, RechiffrementChampsTests.Chiffreur("d2"), Petit);
        relance.Complet.ShouldBeTrue();
        Colonne(relance, "texte_chiffre").Rechiffrees.ShouldBe(1);
    }

    [Fact]
    public async Task Une_erreur_de_base_sur_une_ligne_n_ecarte_que_cette_ligne_sans_journaliser_de_valeur()
    {
        var connexion = await serveur.CreerBaseAsync();
        var bloquee = Guid.CreateVersion7();
        await using (var db = await OuvrirAsync(connexion))
        {
            // Contrainte qui refuse la nouvelle valeur d'une seule ligne (comme une troncature ou une contrainte métier).
            await using (var contrainte = new NpgsqlCommand("ALTER TABLE fiche ADD CONSTRAINT reste_v1 CHECK (libelle <> 'bloquee' OR starts_with(texte_chiffre, 'v1:'))", db))
            {
                await contrainte.ExecuteNonQueryAsync(Ct);
            }

            for (var i = 0; i < 5; i++)
            {
                await InsererAsync(db, i == 2 ? bloquee : Guid.CreateVersion7(), RechiffrementChampsTests.ChiffrerV1($"texte fictif {i}"), null, i == 2 ? "bloquee" : "l");
            }
        }

        var journal = new JournalCapture();
        var rapport = await ExecuterAsync(connexion, RechiffrementChampsTests.Chiffreur("d2"), Petit, journal);

        Colonne(rapport, "texte_chiffre").Rechiffrees.ShouldBe(4);
        Colonne(rapport, "texte_chiffre").Erreurs.ShouldBe(1);
        rapport.ErreursDetaillees.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Cle.ShouldBe(bloquee.ToString()),
            e => e.TypeErreur.ShouldBe("PostgresException"),
            e => e.Message.ShouldBe("Erreur de base de données (SQLSTATE 23514)."));
        rapport.RestantesV1.ShouldBe(1);
        rapport.Complet.ShouldBeFalse();
        journal.Messages.ShouldAllBe(m => !m.Contains("fictif", StringComparison.Ordinal) && !m.Contains("reste_v1", StringComparison.Ordinal)
                                          && !m.Contains("v1:k1:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Une_erreur_sur_une_colonne_entiere_est_comptee_et_les_autres_colonnes_sont_traitees()
    {
        var connexion = await serveur.CreerBaseAsync();
        await using (var db = await OuvrirAsync(connexion))
        {
            await InsererAsync(db, Guid.CreateVersion7(), RechiffrementChampsTests.ChiffrerV1("a"), RechiffrementChampsTests.ChiffrerV1("b"));
        }

        var rapport = await ExecuterAsync(connexion, RechiffrementChampsTests.Chiffreur("d2"), Petit,
            colonnesEnTete: [new ColonneChiffree(null, "fiche", "absente_chiffre", "id")]);

        Colonne(rapport, "absente_chiffre").Erreurs.ShouldBe(1);
        rapport.ErreursDetaillees.ShouldContain(e => e.Colonne == "fiche.absente_chiffre" && e.Message.Contains("42703", StringComparison.Ordinal));
        Colonne(rapport, "texte_chiffre").Rechiffrees.ShouldBe(1);
        Colonne(rapport, "charge_chiffree").Rechiffrees.ShouldBe(1);
        rapport.Verification.ColonnesAbsentes.ShouldBe(["fiche.absente_chiffre"]);
        rapport.Restantes.ShouldBe(0);
        rapport.Complet.ShouldBeFalse();
    }

    private static async Task AjouterColonneJsonAsync(NpgsqlConnection db)
    {
        await using var commande = new NpgsqlCommand("ALTER TABLE fiche ADD COLUMN donnees jsonb", db);
        await commande.ExecuteNonQueryAsync(Ct);
    }

    private static async Task EcrireJsonAsync(NpgsqlConnection db, Guid id, string valeurChiffree)
    {
        await using var commande = new NpgsqlCommand("UPDATE fiche SET donnees = jsonb_build_object('reponse', @v::text) WHERE id = @id", db);
        commande.Parameters.AddWithValue("v", valeurChiffree);
        commande.Parameters.AddWithValue("id", id);
        await commande.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<List<(string Table, string Colonne, long Chiffrees, long V1, long HorsCourante)>> ControlerAsync(string connexion, string requete)
    {
        await using var db = await OuvrirAsync(connexion);
        await using var commande = new NpgsqlCommand(requete, db);
        await using var lecteur = await commande.ExecuteReaderAsync(Ct);
        var lignes = new List<(string, string, long, long, long)>();
        while (await lecteur.ReadAsync(Ct))
        {
            lecteur.GetString(0).ShouldBe("public");
            lignes.Add((lecteur.GetString(1), lecteur.GetString(2), lecteur.GetInt64(3), lecteur.GetInt64(4), lecteur.GetInt64(5)));
        }

        return lignes;
    }

    /// <summary>Bloc SQL de <c>infra/README.md</c> qui commence par le commentaire « Contrôle du rechiffrement ».</summary>
    private static string RequeteDeControle()
    {
        var dossier = new DirectoryInfo(AppContext.BaseDirectory);
        while (dossier is not null && !File.Exists(Path.Combine(dossier.FullName, "Sepp.slnx")))
        {
            dossier = dossier.Parent;
        }

        dossier.ShouldNotBeNull("Racine du dépôt introuvable.");
        var readme = File.ReadAllText(Path.Combine(dossier.FullName, "infra", "README.md")).ReplaceLineEndings("\n");
        var debut = readme.IndexOf("-- Contrôle du rechiffrement (#396, #482)", StringComparison.Ordinal);
        debut.ShouldBeGreaterThanOrEqualTo(0, "Requête de contrôle absente de infra/README.md.");
        var fin = readme.IndexOf("\n```", debut, StringComparison.Ordinal);
        return readme[debut..fin];
    }

    private static IHost Hote(string connexion, FieldEncryptor chiffreur, JournalCapture? journal = null, bool verifierSeulement = false)
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
        builder.Services.AddDbContext<RechiffrementTestDbContext>(o => o.UseNpgsql(connexion));
        return builder.Build();
    }

    private static async Task<RapportRechiffrement> ExecuterAsync(
        string connexion, FieldEncryptor chiffreur, OptionsRechiffrement options, JournalCapture? journal = null, TimeProvider? horloge = null,
        CancellationToken? annulation = null, IReadOnlyList<ColonneChiffree>? colonnesEnTete = null)
    {
        await using var contexte = ServeurPostgresRechiffrement.Contexte(connexion, chiffreur);
        await contexte.Database.OpenConnectionAsync(Ct);
        using var fabrique = LoggerFactory.Create(b => b.AddProvider(journal ?? new JournalCapture()));
        return await RechiffrementChamps.ExecuterAsync(
            contexte.Database.GetDbConnection(), chiffreur, [.. colonnesEnTete ?? [], .. ColonnesChiffrees.Depuis(contexte.Model)], options,
            fabrique.CreateLogger("test"), horloge, annulation ?? Ct);
    }

    private static RapportColonneRechiffrement Colonne(RapportRechiffrement rapport, string colonne) =>
        rapport.Colonnes.Single(c => c.Colonne == $"fiche.{colonne}");

    private static async Task InsererAsync(NpgsqlConnection db, Guid id, string texte, string? charge, string libelle = "l")
    {
        await using var commande = new NpgsqlCommand("INSERT INTO fiche (id, texte_chiffre, charge_chiffree, libelle) VALUES (@id, @t, @c, @l)", db);
        commande.Parameters.AddWithValue("id", id);
        commande.Parameters.AddWithValue("t", texte);
        commande.Parameters.AddWithValue("c", (object?)charge ?? DBNull.Value);
        commande.Parameters.AddWithValue("l", libelle);
        await commande.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<NpgsqlConnection> OuvrirAsync(string connexion)
    {
        var db = new NpgsqlConnection(connexion);
        await db.OpenAsync(Ct);
        return db;
    }

    private static async Task<List<string?>> ValeursAsync(string connexion)
    {
        await using var db = await OuvrirAsync(connexion);
        await using var commande = new NpgsqlCommand("SELECT texte_chiffre, charge_chiffree FROM fiche ORDER BY id", db);
        await using var lecteur = await commande.ExecuteReaderAsync(Ct);
        var valeurs = new List<string?>();
        while (await lecteur.ReadAsync(Ct))
        {
            valeurs.Add(lecteur.GetString(0));
            valeurs.Add(lecteur.IsDBNull(1) ? null : lecteur.GetString(1));
        }

        return valeurs;
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
