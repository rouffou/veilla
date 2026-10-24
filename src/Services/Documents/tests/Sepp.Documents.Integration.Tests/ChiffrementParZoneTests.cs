using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

using Sepp.Documents.Adapters.Chiffrement;
using Sepp.Documents.Application;
using Sepp.Documents.Domain.Commun;

using Shouldly;

namespace Sepp.Documents.Integration.Tests;

/// <summary>
/// ADR 0009, ARC-44, ARC-45 : chiffrement par enveloppe (format <c>SEPPDOC2</c>), clé de données par document enveloppée
/// par la clé racine de la zone, rotation, altération, objet déplacé, lecture du format historique <c>SEPPDOC1</c>.
/// </summary>
public class ChiffrementParZoneTests
{
    private static readonly byte[] Clair = "contenu confidentiel"u8.ToArray();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static string NouvelleCle() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>Clés racines locales v1 des trois zones.</summary>
    private static Dictionary<string, string?> ClesRacines() => new()
    {
        ["Documents:Enveloppe:Type"] = "Local",
        ["Documents:Enveloppe:CleKek:standard:Courante"] = "v1",
        ["Documents:Enveloppe:CleKek:standard:Versions:v1"] = NouvelleCle(),
        ["Documents:Enveloppe:CleKek:medicale:Courante"] = "v1",
        ["Documents:Enveloppe:CleKek:medicale:Versions:v1"] = NouvelleCle(),
        ["Documents:Enveloppe:CleKek:psychosociale:Courante"] = "v1",
        ["Documents:Enveloppe:CleKek:psychosociale:Versions:v1"] = NouvelleCle(),
    };

    private static IConfiguration Configuration(Dictionary<string, string?> valeurs) => new ConfigurationBuilder().AddInMemoryCollection(valeurs).Build();

    private static ChiffrementParZone Local(Dictionary<string, string?> valeurs, params ZoneDocument[] zones)
    {
        var configuration = Configuration(valeurs);
        var servies = new ZonesFixes(zones.Length == 0 ? Zones.Toutes : zones);
        var enveloppe = EnveloppeLocale.Depuis(servies.Zones.ToDictionary(z => z, z => ConfigurationEnveloppe.SectionCle(configuration, servies, z)));
        return new ChiffrementParZone(enveloppe, configuration, servies);
    }

    private sealed class ZonesFixes(IReadOnlyCollection<ZoneDocument> zones) : IZonesServies
    {
        public IReadOnlyCollection<ZoneDocument> Zones { get; } = zones;

        public bool Sert(ZoneDocument zone) => Zones.Contains(zone);
    }

    /// <summary>Découpe d'un objet SEPPDOC2 : position et longueur du kid, de la clé enveloppée, puis du nonce.</summary>
    private static (int Kid, int LongueurKid, int Enveloppe, int LongueurEnveloppe, int Nonce) Structure(byte[] objet)
    {
        var longueurKid = BinaryPrimitives.ReadUInt16BigEndian(objet.AsSpan(8, 2));
        var positionEnveloppe = 8 + 2 + longueurKid + 2;
        var longueurEnveloppe = BinaryPrimitives.ReadUInt16BigEndian(objet.AsSpan(positionEnveloppe - 2, 2));
        return (10, longueurKid, positionEnveloppe, longueurEnveloppe, positionEnveloppe + longueurEnveloppe);
    }

    [Fact]
    public async Task Le_contenu_chiffre_par_enveloppe_se_dechiffre_avec_la_cle_racine_de_sa_zone()
    {
        var chiffrement = Local(ClesRacines());
        var id = Guid.CreateVersion7();

        var chiffre = await chiffrement.ChiffrerAsync(ZoneDocument.Medicale, id, Clair, _ct);

        chiffre.CleId.ShouldBe("local:medicale:v1");
        Encoding.ASCII.GetString(chiffre.Octets[..8]).ShouldBe("SEPPDOC2");
        Encoding.UTF8.GetString(chiffre.Octets).ShouldNotContain("confidentiel");
        (await chiffrement.DechiffrerAsync(ZoneDocument.Medicale, id, chiffre.Octets, _ct)).ShouldBe(Clair);
    }

    [Fact]
    public async Task Chaque_zone_enveloppe_avec_sa_propre_cle_racine()
    {
        var chiffrement = Local(ClesRacines());
        var id = Guid.CreateVersion7();

        (await chiffrement.ChiffrerAsync(ZoneDocument.Standard, id, Clair, _ct)).CleId.ShouldBe("local:standard:v1");
        (await chiffrement.ChiffrerAsync(ZoneDocument.Medicale, id, Clair, _ct)).CleId.ShouldBe("local:medicale:v1");
        (await chiffrement.ChiffrerAsync(ZoneDocument.Psychosociale, id, Clair, _ct)).CleId.ShouldBe("local:psychosociale:v1");
    }

    [Fact]
    public async Task Deux_chiffrements_du_meme_contenu_utilisent_des_cles_de_donnees_et_des_nonces_differents()
    {
        var chiffrement = Local(ClesRacines());
        var id = Guid.CreateVersion7();

        var premier = (await chiffrement.ChiffrerAsync(ZoneDocument.Standard, id, Clair, _ct)).Octets;
        var second = (await chiffrement.ChiffrerAsync(ZoneDocument.Standard, id, Clair, _ct)).Octets;

        var (_, _, enveloppe, longueur, nonce) = Structure(premier);
        premier.AsSpan(enveloppe, longueur).SequenceEqual(second.AsSpan(enveloppe, longueur)).ShouldBeFalse();
        premier.AsSpan(nonce, 12).SequenceEqual(second.AsSpan(nonce, 12)).ShouldBeFalse();
    }

    [Fact]
    public async Task Un_objet_copie_vers_une_autre_zone_ou_un_autre_document_est_refuse()
    {
        var chiffrement = Local(ClesRacines());
        var id = Guid.CreateVersion7();
        var chiffre = (await chiffrement.ChiffrerAsync(ZoneDocument.Medicale, id, Clair, _ct)).Octets;

        await Should.ThrowAsync<ContenuAltereException>(() => chiffrement.DechiffrerAsync(ZoneDocument.Standard, id, chiffre, _ct));
        await Should.ThrowAsync<ContenuAltereException>(() => chiffrement.DechiffrerAsync(ZoneDocument.Psychosociale, id, chiffre, _ct));
        await Should.ThrowAsync<ContenuAltereException>(() => chiffrement.DechiffrerAsync(ZoneDocument.Medicale, Guid.CreateVersion7(), chiffre, _ct));
    }

    [Fact]
    public async Task L_alteration_de_la_cle_enveloppee_du_kid_de_l_etiquette_ou_du_contenu_est_detectee()
    {
        var valeurs = ClesRacines();
        valeurs["Documents:Enveloppe:CleKek:standard:Versions:v2"] = NouvelleCle();
        var chiffrement = Local(valeurs);
        var id = Guid.CreateVersion7();
        var chiffre = (await chiffrement.ChiffrerAsync(ZoneDocument.Standard, id, Clair, _ct)).Octets;
        var (kid, longueurKid, enveloppe, _, nonce) = Structure(chiffre);

        byte[] Altere(int position)
        {
            var copie = (byte[])chiffre.Clone();
            copie[position] ^= 0x01;
            return copie;
        }

        // Kid pointant vers une autre version existante de la clé racine (« local:standard:v1 » → « local:standard:v2 »).
        var autreVersion = (byte[])chiffre.Clone();
        autreVersion[kid + longueurKid - 1] = (byte)'2';

        await Should.ThrowAsync<ContenuAltereException>(() => chiffrement.DechiffrerAsync(ZoneDocument.Standard, id, Altere(enveloppe + 3), _ct));
        await Should.ThrowAsync<ContenuAltereException>(() => chiffrement.DechiffrerAsync(ZoneDocument.Standard, id, Altere(kid + 2), _ct));
        await Should.ThrowAsync<ContenuAltereException>(() => chiffrement.DechiffrerAsync(ZoneDocument.Standard, id, autreVersion, _ct));
        await Should.ThrowAsync<ContenuAltereException>(() => chiffrement.DechiffrerAsync(ZoneDocument.Standard, id, Altere(nonce + 12), _ct));
        await Should.ThrowAsync<ContenuAltereException>(() => chiffrement.DechiffrerAsync(ZoneDocument.Standard, id, Altere(nonce), _ct));
        await Should.ThrowAsync<ContenuAltereException>(() => chiffrement.DechiffrerAsync(ZoneDocument.Standard, id, Altere(chiffre.Length - 1), _ct));
    }

    [Fact]
    public async Task Un_contenu_tronque_ou_de_format_inconnu_est_detecte()
    {
        var chiffrement = Local(ClesRacines());
        var id = Guid.CreateVersion7();
        var chiffre = (await chiffrement.ChiffrerAsync(ZoneDocument.Standard, id, Clair, _ct)).Octets;

        await Should.ThrowAsync<ContenuAltereException>(() => chiffrement.DechiffrerAsync(ZoneDocument.Standard, id, chiffre[..20], _ct));
        await Should.ThrowAsync<ContenuAltereException>(() => chiffrement.DechiffrerAsync(ZoneDocument.Standard, id, chiffre[..(Structure(chiffre).Nonce + 10)], _ct));
        await Should.ThrowAsync<ContenuAltereException>(() => chiffrement.DechiffrerAsync(ZoneDocument.Standard, id, [1, 2, 3], _ct));
    }

    [Fact]
    public async Task La_rotation_de_la_cle_racine_locale_conserve_la_lecture_des_documents_enveloppes_par_l_ancienne_version()
    {
        var valeurs = ClesRacines();
        var id = Guid.CreateVersion7();
        var ancien = (await Local(valeurs).ChiffrerAsync(ZoneDocument.Standard, id, Clair, _ct)).Octets;

        valeurs["Documents:Enveloppe:CleKek:standard:Versions:v2"] = NouvelleCle();
        valeurs["Documents:Enveloppe:CleKek:standard:Courante"] = "v2";
        var apresRotation = Local(valeurs);

        (await apresRotation.DechiffrerAsync(ZoneDocument.Standard, id, ancien, _ct)).ShouldBe(Clair);
        (await apresRotation.ChiffrerAsync(ZoneDocument.Standard, id, Clair, _ct)).CleId.ShouldBe("local:standard:v2");
    }

    [Fact]
    public async Task Le_format_historique_SEPPDOC1_reste_lisible_si_l_ancienne_cle_est_configuree()
    {
        var ancienneCle = RandomNumberGenerator.GetBytes(32);
        var id = Guid.CreateVersion7();
        var historique = AncienFormat(ZoneDocument.Medicale, id, "med-1", ancienneCle, Clair);

        var avecAncienneCle = ClesRacines();
        avecAncienneCle["Documents:Chiffrement:medicale:Encryption:Keys:med-1"] = Convert.ToBase64String(ancienneCle);

        (await Local(avecAncienneCle).DechiffrerAsync(ZoneDocument.Medicale, id, historique, _ct)).ShouldBe(Clair);
        await Should.ThrowAsync<ContenuAltereException>(() => Local(avecAncienneCle).DechiffrerAsync(ZoneDocument.Medicale, Guid.CreateVersion7(), historique, _ct));
        await Should.ThrowAsync<ContenuAltereException>(() => Local(ClesRacines()).DechiffrerAsync(ZoneDocument.Medicale, id, historique, _ct));

        // Les nouveaux documents sont toujours enveloppés, jamais chiffrés avec l'ancienne clé.
        (await Local(avecAncienneCle).ChiffrerAsync(ZoneDocument.Medicale, id, Clair, _ct)).CleId.ShouldBe("local:medicale:v1");
    }

    [Fact]
    public void Une_meme_cle_racine_ou_ancienne_cle_configuree_pour_deux_zones_est_refusee_au_demarrage()
    {
        var racine = ClesRacines();
        racine["Documents:Enveloppe:CleKek:medicale:Versions:v1"] = racine["Documents:Enveloppe:CleKek:standard:Versions:v1"];
        Should.Throw<InvalidOperationException>(() => Local(racine)).Message.ShouldContain("une clé par zone");

        var ancienne = ClesRacines();
        var partagee = NouvelleCle();
        ancienne["Documents:Chiffrement:standard:Encryption:Keys:std-1"] = partagee;
        ancienne["Documents:Chiffrement:medicale:Encryption:Keys:med-1"] = partagee;
        Should.Throw<InvalidOperationException>(() => Local(ancienne)).Message.ShouldContain("une clé par zone");
    }

    [Fact]
    public void Une_zone_servie_sans_cle_racine_est_refusee_au_demarrage()
    {
        var valeurs = ClesRacines();
        valeurs.Remove("Documents:Enveloppe:CleKek:psychosociale:Courante");
        valeurs.Remove("Documents:Enveloppe:CleKek:psychosociale:Versions:v1");

        Should.Throw<InvalidOperationException>(() => Local(valeurs)).Message.ShouldContain("CleKek:psychosociale");
    }

    [Fact]
    public async Task Une_instance_d_une_seule_zone_lit_la_cle_racine_sans_zone_et_ne_detient_aucune_autre_cle()
    {
        var valeurs = new Dictionary<string, string?>
        {
            ["Documents:Enveloppe:CleKek:Courante"] = "v1",
            ["Documents:Enveloppe:CleKek:Versions:v1"] = NouvelleCle(),
        };
        var chiffrement = Local(valeurs, ZoneDocument.Medicale);
        var id = Guid.CreateVersion7();

        var chiffre = await chiffrement.ChiffrerAsync(ZoneDocument.Medicale, id, Clair, _ct);

        (await chiffrement.DechiffrerAsync(ZoneDocument.Medicale, id, chiffre.Octets, _ct)).ShouldBe(Clair);
        await Should.ThrowAsync<InvalidOperationException>(() => chiffrement.ChiffrerAsync(ZoneDocument.Standard, id, Clair, _ct));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Inconnu")]
    public void Un_type_d_enveloppe_absent_ou_inconnu_est_refuse(string? type)
    {
        var valeurs = ClesRacines();
        valeurs["Documents:Enveloppe:Type"] = type;

        Should.Throw<InvalidOperationException>(() => Ajouter(valeurs, "Development")).Message.ShouldContain("Documents:Enveloppe:Type");
    }

    [Fact]
    public void L_enveloppe_locale_est_refusee_hors_developpement_et_test()
    {
        Should.Throw<InvalidOperationException>(() => Ajouter(ClesRacines(), "Production")).Message.ShouldContain("ARC-44");
    }

    [Theory]
    [InlineData("https://kv-sepp-med.vault.azure.net/keys/documents-kek/0123456789abcdef0123456789abcdef")]
    [InlineData("http://kv-sepp-med.vault.azure.net/keys/documents-kek")]
    [InlineData("pas-une-uri")]
    public void L_uri_de_la_cle_racine_key_vault_doit_etre_https_et_sans_version(string uri)
    {
        var valeurs = new Dictionary<string, string?> { ["Documents:Enveloppe:Type"] = "KeyVault", ["Documents:Enveloppe:CleKek"] = uri };

        Should.Throw<InvalidOperationException>(() => Ajouter(valeurs, "Production", ZoneDocument.Medicale)).Message.ShouldContain("CleKek");
    }

    [Theory]
    [InlineData("https://kv-sepp-med.vault.azure.net/keys/documents-kek")]
    [InlineData("https://hsm-sepp-med.managedhsm.azure.net/keys/documents-kek")]
    public void L_uri_sans_version_d_un_key_vault_ou_d_un_managed_hsm_est_acceptee(string uri)
    {
        var valeurs = new Dictionary<string, string?> { ["Documents:Enveloppe:Type"] = "KeyVault", ["Documents:Enveloppe:CleKek"] = uri };

        Ajouter(valeurs, "Production", ZoneDocument.Medicale).ShouldBeOfType<EnveloppeKeyVault>();
    }

    private static IEnveloppeCles Ajouter(Dictionary<string, string?> valeurs, string environnement, params ZoneDocument[] zones)
    {
        var services = new ServiceCollection();
        ConfigurationEnveloppe.AjouterEnveloppe(services, Configuration(valeurs), new Environnement(environnement),
            new ZonesFixes(zones.Length == 0 ? Zones.Toutes : zones));
        return services.BuildServiceProvider().GetRequiredService<IEnveloppeCles>();
    }

    private sealed class Environnement(string nom) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = nom;

        public string ApplicationName { get; set; } = "documents";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>Reproduction de l'ancien chiffreur (<c>SEPPDOC1</c>, clé AES de zone sans enveloppe).</summary>
    private static byte[] AncienFormat(ZoneDocument zone, Guid documentId, string cleId, byte[] cle, byte[] clair)
    {
        var signature = "SEPPDOC1"u8.ToArray();
        var id = Encoding.UTF8.GetBytes(cleId);
        var entete = signature.Length + 1 + id.Length;
        var sortie = new byte[entete + 12 + 16 + clair.Length];
        signature.CopyTo(sortie, 0);
        sortie[signature.Length] = (byte)id.Length;
        id.CopyTo(sortie, signature.Length + 1);
        RandomNumberGenerator.Fill(sortie.AsSpan(entete, 12));
        var texte = Encoding.UTF8.GetBytes($"documents|{zone.Code()}|{documentId:N}|{cleId}");
        var aad = new byte[4 + texte.Length];
        BinaryPrimitives.WriteInt32BigEndian(aad, texte.Length);
        texte.CopyTo(aad, 4);
        using var aes = new AesGcm(cle, 16);
        aes.Encrypt(sortie.AsSpan(entete, 12), clair, sortie.AsSpan(entete + 12 + 16), sortie.AsSpan(entete + 12, 16), aad);
        return sortie;
    }
}
