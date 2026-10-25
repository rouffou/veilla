using System.Security.Cryptography;
using System.Text;

using Azure;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;

using Sepp.BuildingBlocks.Infrastructure.Security;

using Shouldly;

namespace Sepp.BuildingBlocks.Tests;

/// <summary>
/// ARC-44, ARC-45, ADR 0012 : chiffrement des champs par enveloppe. DEK déballées au chargement (clé racine locale ou
/// coffre simulé), rotation de la clé racine et des DEK, relecture du format historique v1, altérations détectées et
/// index aveugle inchangé par l'enveloppe.
/// </summary>
public class FieldEncryptionTests
{
    private const string Section = "Encryption";
    private const string CleCoffre = "https://kv-sepp-std.vault.azure.net/keys/personnes-fields-kek";

    private static readonly HostingEnvironment Test = new() { EnvironmentName = "Testing" };

    private static readonly byte[] KekV1 = RandomNumberGenerator.GetBytes(32);
    private static readonly byte[] KekV2 = RandomNumberGenerator.GetBytes(32);

    private static CleRacineLocale KekLocale(string courante) =>
        new("local:field-kek:", "field-kek", courante, new Dictionary<string, byte[]> { ["v1"] = KekV1, ["v2"] = KekV2 });

    private static string Envelopper(CleRacineLocale kek, byte[] cle) =>
        ChiffrementChamps.Formater(kek.EnvelopperAsync(cle, CancellationToken.None).GetAwaiter().GetResult());

    private static IConfigurationSection Configuration(Dictionary<string, string?> valeurs) =>
        new ConfigurationBuilder().AddInMemoryCollection(valeurs).Build().GetSection(Section);

    /// <summary>Configuration locale : KEK v1 et v2 (courante donnée), DEK données (identifiant -> valeur enveloppée).</summary>
    private static Dictionary<string, string?> Locale(string kekCourante, string dekCourante, Dictionary<string, string> deks)
    {
        var valeurs = new Dictionary<string, string?>
        {
            [$"{Section}:Enveloppe:Type"] = "Local",
            [$"{Section}:Enveloppe:CleKek:Courante"] = kekCourante,
            [$"{Section}:Enveloppe:CleKek:Versions:v1"] = Convert.ToBase64String(KekV1),
            [$"{Section}:Enveloppe:CleKek:Versions:v2"] = Convert.ToBase64String(KekV2),
            [$"{Section}:CurrentKeyId"] = dekCourante,
        };
        foreach (var (id, valeur) in deks)
        {
            valeurs[$"{Section}:Deks:{id}"] = valeur;
        }

        return valeurs;
    }

    private static FieldEncryptor Encryptor(Dictionary<string, string?> valeurs) => new(ChiffrementChamps.Charger(Configuration(valeurs), Test).Cles);

    private static FieldEncryptor Simple() =>
        Encryptor(Locale("v1", "d1", new() { ["d1"] = Envelopper(KekLocale("v1"), RandomNumberGenerator.GetBytes(32)) }));

    [Fact]
    public void Un_champ_chiffre_se_dechiffre_et_ne_contient_pas_le_clair()
    {
        var encryptor = Simple();

        var chiffre = encryptor.Encrypt("85073003328");

        chiffre.ShouldStartWith("v2:d1:");
        chiffre.ShouldNotContain("85073003328");
        encryptor.Decrypt(chiffre).ShouldBe("85073003328");
    }

    [Fact]
    public void Deux_chiffrements_du_meme_texte_different()
    {
        var encryptor = Simple();

        encryptor.Encrypt("x").ShouldNotBe(encryptor.Encrypt("x"));
    }

    [Fact]
    public void Une_alteration_du_champ_est_detectee()
    {
        var encryptor = Simple();
        var chiffre = encryptor.Encrypt("anamnèse");
        var bytes = Convert.FromBase64String(chiffre.Split(':')[2]);
        bytes[^1] ^= 0x01;

        Should.Throw<CryptographicException>(() => encryptor.Decrypt($"v2:d1:{Convert.ToBase64String(bytes)}"));
    }

    [Fact]
    public void Changer_l_identifiant_de_cle_ou_la_version_du_format_est_detecte()
    {
        var kek = KekLocale("v1");
        var dek = RandomNumberGenerator.GetBytes(32);
        var deuxIds = Encryptor(Locale("v1", "d1", new() { ["d1"] = Envelopper(kek, dek), ["d2"] = Envelopper(kek, dek) }));
        var chiffre = deuxIds.Encrypt("secret");

        // Même DEK sous un autre identifiant : les données associées (v2:<id>) ne correspondent plus.
        Should.Throw<CryptographicException>(() => deuxIds.Decrypt(chiffre.Replace("v2:d1:", "v2:d2:", StringComparison.Ordinal)));
        Should.Throw<CryptographicException>(() => deuxIds.Decrypt(chiffre.Replace("v2:", "v3:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Apres_rotation_des_cles_racines_et_des_DEK_les_anciennes_valeurs_restent_lisibles()
    {
        // Avant : KEK v1, DEK d1 enveloppée par v1.
        var dek1 = Envelopper(KekLocale("v1"), RandomNumberGenerator.GetBytes(32));
        var avant = Encryptor(Locale("v1", "d1", new() { ["d1"] = dek1 }));
        var ancien = avant.Encrypt("dossier");

        // Rotation de la KEK (v2 courante), puis nouvelle DEK d2 enveloppée par v2 : d1 reste déballée par v1.
        var dek2 = Envelopper(KekLocale("v2"), RandomNumberGenerator.GetBytes(32));
        dek2.ShouldStartWith("local:field-kek:v2|");
        var apres = Encryptor(Locale("v2", "d2", new() { ["d1"] = dek1, ["d2"] = dek2 }));
        var nouveau = apres.Encrypt("dossier");

        nouveau.ShouldStartWith("v2:d2:");
        apres.Decrypt(ancien).ShouldBe("dossier");
        apres.Decrypt(nouveau).ShouldBe("dossier");
        apres.NeedsRotation(ancien).ShouldBeTrue();
        apres.NeedsRotation(nouveau).ShouldBeFalse();
        Should.Throw<CryptographicException>(() => avant.Decrypt(nouveau));
    }

    [Fact]
    public void Le_format_historique_v1_reste_lisible_si_ses_cles_sont_configurees_mais_n_est_plus_ecrit()
    {
        var historique = RandomNumberGenerator.GetBytes(32);
        var ancien = ChiffrerV1("k1", historique, "85073003328");
        var valeurs = Locale("v1", "d1", new() { ["d1"] = Envelopper(KekLocale("v1"), RandomNumberGenerator.GetBytes(32)) });

        Should.Throw<CryptographicException>(() => Encryptor(valeurs).Decrypt(ancien));

        valeurs[$"{Section}:Keys:k1"] = Convert.ToBase64String(historique);
        var encryptor = Encryptor(valeurs);
        encryptor.Decrypt(ancien).ShouldBe("85073003328");
        encryptor.NeedsRotation(ancien).ShouldBeTrue();
        encryptor.Encrypt("85073003328").ShouldStartWith("v2:d1:");
    }

    [Fact]
    public void Une_DEK_enveloppee_alteree_ou_etrangere_est_refusee_au_chargement()
    {
        var dek = Envelopper(KekLocale("v1"), RandomNumberGenerator.GetBytes(32));
        var octets = Convert.FromBase64String(dek.Split('|')[1]);
        octets[^1] ^= 0x01;
        var alteree = $"local:field-kek:v1|{Convert.ToBase64String(octets)}";

        Should.Throw<EnveloppeAltereeException>(() => Encryptor(Locale("v1", "d1", new() { ["d1"] = alteree })));
        Should.Throw<EnveloppeAltereeException>(() => Encryptor(Locale("v1", "d1", new() { ["d1"] = dek.Replace(":v1|", ":v9|", StringComparison.Ordinal) })));

        // Une DEK enveloppée par la clé racine d'un autre service ne se déballe pas.
        var autreService = new CleRacineLocale("local:field-kek:", "field-kek", "v1", new Dictionary<string, byte[]> { ["v1"] = RandomNumberGenerator.GetBytes(32) });
        Should.Throw<EnveloppeAltereeException>(() => Encryptor(Locale("v1", "d1", new() { ["d1"] = Envelopper(autreService, RandomNumberGenerator.GetBytes(32)) })));
    }

    [Fact]
    public void Une_configuration_incomplete_est_refusee()
    {
        var dek = Envelopper(KekLocale("v1"), RandomNumberGenerator.GetBytes(32));

        Should.Throw<InvalidOperationException>(() => Encryptor(Locale("v1", "d2", new() { ["d1"] = dek }))).Message.ShouldContain("Deks:d2");
        Should.Throw<InvalidOperationException>(() => Encryptor(Locale("v1", "d1", new() { ["d1"] = "sans-separateur" })));
        Should.Throw<InvalidOperationException>(() => Encryptor(Locale("v1", "d1", new() { ["d1"] = Envelopper(KekLocale("v1"), new byte[16]) }))).Message.ShouldContain("256 bits");

        var sansType = Locale("v1", "d1", new() { ["d1"] = dek });
        sansType.Remove($"{Section}:Enveloppe:Type");
        Should.Throw<InvalidOperationException>(() => Encryptor(sansType)).Message.ShouldContain("Encryption:Enveloppe:Type");

        var historiqueCourt = Locale("v1", "d1", new() { ["d1"] = dek });
        historiqueCourt[$"{Section}:Keys:k1"] = Convert.ToBase64String(new byte[16]);
        Should.Throw<InvalidOperationException>(() => Encryptor(historiqueCourt));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void La_cle_racine_locale_est_refusee_hors_developpement_et_test(string environnement)
    {
        var valeurs = Locale("v1", "d1", new() { ["d1"] = Envelopper(KekLocale("v1"), RandomNumberGenerator.GetBytes(32)) });

        Should.Throw<InvalidOperationException>(() => ChiffrementChamps.Charger(Configuration(valeurs), new HostingEnvironment { EnvironmentName = environnement }))
            .Message.ShouldContain("ARC-44");
    }

    [Fact]
    public async Task Avec_le_coffre_les_DEK_sont_deballees_une_fois_au_chargement_et_survivent_a_la_rotation_de_la_cle_racine()
    {
        var coffre = new CoffreSimule();
        var kek = new CleRacineKeyVault(new Uri(CleCoffre), coffre, "test");
        var dek1 = ChiffrementChamps.Formater(await kek.EnvelopperAsync(RandomNumberGenerator.GetBytes(32), TestContext.Current.CancellationToken));
        var versionInitiale = coffre.Courante;
        coffre.Tourner();
        var dek2 = ChiffrementChamps.Formater(await kek.EnvelopperAsync(RandomNumberGenerator.GetBytes(32), TestContext.Current.CancellationToken));
        dek1.ShouldStartWith($"{CleCoffre}/{versionInitiale}|");
        dek2.ShouldStartWith($"{CleCoffre}/{coffre.Courante}|");

        var avant = new FieldEncryptor(ChiffrementChamps.Charger(Configuration(Coffre("d1", ("d1", dek1))), new HostingEnvironment { EnvironmentName = "Production" }, coffre).Cles);
        var ancien = avant.Encrypt("85073003328");
        coffre.Deballages.Clear();

        var apres = new FieldEncryptor(ChiffrementChamps.Charger(Configuration(Coffre("d2", ("d1", dek1), ("d2", dek2))), new HostingEnvironment { EnvironmentName = "Production" }, coffre).Cles);
        coffre.Deballages.Count.ShouldBe(2);
        for (var i = 0; i < 50; i++)
        {
            apres.Decrypt(apres.Encrypt($"valeur {i}")).ShouldBe($"valeur {i}");
        }

        apres.Decrypt(ancien).ShouldBe("85073003328");
        coffre.Deballages.Count.ShouldBe(2, "Aucun appel au coffre par valeur : les DEK sont en mémoire.");
    }

    [Theory]
    [InlineData("https://kv-pirate.vault.azure.net/keys/personnes-fields-kek/")]
    [InlineData("https://kv-sepp-std.vault.azure.net/keys/integrations-fields-kek/")]
    [InlineData("https://kv-sepp-std.vault.azure.net/keys/personnes-fields-kek/../../secrets/x/")]
    public async Task Un_kid_etranger_a_la_cle_racine_du_service_est_refuse_sans_appel_au_coffre(string prefixe)
    {
        var coffre = new CoffreSimule();
        var kek = new CleRacineKeyVault(new Uri(CleCoffre), coffre, "test");
        var dek = await kek.EnvelopperAsync(RandomNumberGenerator.GetBytes(32), TestContext.Current.CancellationToken);
        var detournee = $"{prefixe}{coffre.Courante}|{Convert.ToBase64String(dek.Octets)}";

        Should.Throw<EnveloppeAltereeException>(() => ChiffrementChamps.Charger(Configuration(Coffre("d1", ("d1", detournee))), Test, coffre));
        coffre.Deballages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Les_refus_du_coffre_sont_classes_alteration_ou_erreur_d_exploitation()
    {
        var coffre = new CoffreSimule();
        var kek = new CleRacineKeyVault(new Uri(CleCoffre), coffre, "test");
        var dek = await kek.EnvelopperAsync(RandomNumberGenerator.GetBytes(32), TestContext.Current.CancellationToken);
        var alteree = (byte[])dek.Octets.Clone();
        alteree[10] ^= 0x01;

        (await Should.ThrowAsync<EnveloppeAltereeException>(() => kek.DesenvelopperAsync(dek.Kid, alteree, TestContext.Current.CancellationToken)))
            .InnerException.ShouldBeOfType<RequestFailedException>().Status.ShouldBe(400);

        coffre.AccesRetire = true;
        (await Should.ThrowAsync<CleRacineInaccessibleException>(() => kek.DesenvelopperAsync(dek.Kid, dek.Octets, TestContext.Current.CancellationToken)))
            .Cause.ShouldBe(CauseCleRacineInaccessible.AccesRefuse);

        coffre.AccesRetire = false;
        var version = coffre.Courante;
        coffre.Tourner();
        coffre.Supprimer(version);
        var introuvable = await Should.ThrowAsync<CleRacineInaccessibleException>(() => kek.DesenvelopperAsync(dek.Kid, dek.Octets, TestContext.Current.CancellationToken));
        introuvable.Cause.ShouldBe(CauseCleRacineInaccessible.Introuvable);
        introuvable.Kid.ShouldBe(dek.Kid);
    }

    [Theory]
    [InlineData("http://kv-sepp-std.vault.azure.net/keys/personnes-fields-kek")]
    [InlineData("https://kv-sepp-std.vault.azure.net/keys/personnes-fields-kek/0123456789abcdef")]
    [InlineData("https://kv-sepp-std.vault.azure.net/secrets/personnes-fields-kek")]
    [InlineData("pas une uri")]
    public void Une_uri_de_cle_racine_invalide_ou_versionnee_est_refusee(string uri) =>
        Should.Throw<InvalidOperationException>(() => ChiffrementChamps.Charger(Configuration(new Dictionary<string, string?>
        {
            [$"{Section}:Enveloppe:Type"] = "KeyVault",
            [$"{Section}:Enveloppe:CleKek"] = uri,
            [$"{Section}:CurrentKeyId"] = "d1",
        }), Test, new CoffreSimule())).Message.ShouldContain("Encryption:Enveloppe:CleKek");

    [Fact]
    public void L_index_aveugle_est_stable_normalise_et_depend_de_la_cle()
    {
        var cleA = RandomNumberGenerator.GetBytes(32);
        var index = new BlindIndex(cleA);

        index.Compute("85.07.30-033.28").ShouldBe(index.Compute("85073003328"));
        index.Compute("85073003328").ShouldNotBe(new BlindIndex(RandomNumberGenerator.GetBytes(32)).Compute("85073003328"));
    }

    [Fact]
    public void La_cle_d_index_aveugle_enveloppee_donne_le_meme_index_que_la_cle_en_clair_apres_rotation_de_la_cle_racine()
    {
        // Migration (ADR 0012) : la clé existante est enveloppée telle quelle ; la recherche par NISS reste fonctionnelle.
        var cleIndex = RandomNumberGenerator.GetBytes(32);
        var avant = new BlindIndex(cleIndex).Compute("85073003328");
        var enveloppee = Envelopper(KekLocale("v1"), cleIndex);
        var chiffrement = ChiffrementChamps.Charger(Configuration(Locale("v2", "d1", new() { ["d1"] = Envelopper(KekLocale("v2"), RandomNumberGenerator.GetBytes(32)) })), Test);

        new BlindIndex(chiffrement.Desenvelopper(enveloppee, "BlindIndex:CleEnveloppee")).Compute("85.07.30-033.28").ShouldBe(avant);
    }

    [Fact]
    public void La_configuration_locale_de_test_est_complete_et_enveloppe_les_autres_cles()
    {
        var cleIndex = RandomNumberGenerator.GetBytes(32);
        var valeurs = ChiffrementChamps.ConfigurationLocale("ZoneMedicale:Encryption", "t1", new Dictionary<string, byte[]> { ["BlindIndex:CleEnveloppee"] = cleIndex });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(valeurs.Select(v => KeyValuePair.Create(v.Key, (string?)v.Value))).Build();

        var chiffrement = ChiffrementChamps.Charger(configuration.GetSection("ZoneMedicale:Encryption"), Test);

        chiffrement.EstLocal.ShouldBeTrue();
        new FieldEncryptor(chiffrement.Cles).Encrypt("x").ShouldStartWith("v2:t1:");
        chiffrement.Desenvelopper(configuration["BlindIndex:CleEnveloppee"], "BlindIndex:CleEnveloppee").ShouldBe(cleIndex);
    }

    private static Dictionary<string, string?> Coffre(string courante, params (string Id, string Valeur)[] deks)
    {
        var valeurs = new Dictionary<string, string?>
        {
            [$"{Section}:Enveloppe:Type"] = "KeyVault",
            [$"{Section}:Enveloppe:CleKek"] = CleCoffre,
            [$"{Section}:CurrentKeyId"] = courante,
        };
        foreach (var (id, valeur) in deks)
        {
            valeurs[$"{Section}:Deks:{id}"] = valeur;
        }

        return valeurs;
    }

    /// <summary>Valeur au format historique v1 (clé AES en clair, données associées = identifiant de clé).</summary>
    private static string ChiffrerV1(string keyId, byte[] cle, string clair)
    {
        var data = Encoding.UTF8.GetBytes(clair);
        var output = new byte[12 + data.Length + 16];
        RandomNumberGenerator.Fill(output.AsSpan(0, 12));
        using var aes = new AesGcm(cle, 16);
        aes.Encrypt(output.AsSpan(0, 12), data, output.AsSpan(12, data.Length), output.AsSpan(12 + data.Length), Encoding.UTF8.GetBytes(keyId));
        return $"v1:{keyId}:{Convert.ToBase64String(output)}";
    }

    /// <summary>
    /// Coffre simulé : une clé RSA 3072 par version, la dernière enveloppe, toutes déballent ; refus comme Azure Key Vault
    /// (404 version supprimée, 400 clé enveloppée invalide, 403 accès retiré).
    /// </summary>
    private sealed class CoffreSimule : IOperationsCleRacine
    {
        private readonly Dictionary<string, RSA> _versions = new(StringComparer.Ordinal);

        public CoffreSimule() => Tourner();

        public string Courante { get; private set; } = string.Empty;

        public List<Uri> Deballages { get; } = [];

        public bool AccesRetire { get; set; }

        public void Tourner()
        {
            Courante = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            _versions[Courante] = RSA.Create(3072);
        }

        public void Supprimer(string version) => _versions.Remove(version);

        public Task<CleDonneesEnveloppee> EnvelopperAsync(Uri cleSansVersion, byte[] cleDonnees, CancellationToken cancellationToken) =>
            Task.FromResult(new CleDonneesEnveloppee($"{cleSansVersion.AbsoluteUri}/{Courante}", _versions[Courante].Encrypt(cleDonnees, RSAEncryptionPadding.OaepSHA256)));

        public Task<byte[]> DesenvelopperAsync(Uri kid, byte[] cleEnveloppee, CancellationToken cancellationToken)
        {
            Deballages.Add(kid);
            if (AccesRetire)
            {
                throw new RequestFailedException(403, "Forbidden", "Forbidden", null);
            }

            if (!_versions.TryGetValue(kid.Segments[^1], out var rsa))
            {
                throw new RequestFailedException(404, "Key not found", "KeyNotFound", null);
            }

            try
            {
                return Task.FromResult(rsa.Decrypt(cleEnveloppee, RSAEncryptionPadding.OaepSHA256));
            }
            catch (CryptographicException ex)
            {
                throw new RequestFailedException(400, "Bad request", "BadParameter", ex);
            }
        }
    }
}
