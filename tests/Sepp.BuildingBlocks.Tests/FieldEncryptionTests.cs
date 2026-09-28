using System.Security.Cryptography;

using Microsoft.Extensions.Configuration;

using Sepp.BuildingBlocks.Infrastructure.Security;

using Shouldly;

namespace Sepp.BuildingBlocks.Tests;

public class FieldEncryptionTests
{
    private static readonly string KeyA = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static readonly string KeyB = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static FieldEncryptor Encryptor(string current) => new(new ConfigurationFieldKeyProvider(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:CurrentKeyId"] = current,
            ["Encryption:Keys:a"] = KeyA,
            ["Encryption:Keys:b"] = KeyB,
        }).Build()));

    [Fact]
    public void Un_champ_chiffre_se_dechiffre_et_ne_contient_pas_le_clair()
    {
        var encryptor = Encryptor("a");

        var chiffre = encryptor.Encrypt("85073003328");

        chiffre.ShouldStartWith("v1:a:");
        chiffre.ShouldNotContain("85073003328");
        encryptor.Decrypt(chiffre).ShouldBe("85073003328");
    }

    [Fact]
    public void Deux_chiffrements_du_meme_texte_different() =>
        Encryptor("a").Encrypt("x").ShouldNotBe(Encryptor("a").Encrypt("x"));

    [Fact]
    public void Une_alteration_est_detectee()
    {
        var chiffre = Encryptor("a").Encrypt("anamnèse");
        var bytes = Convert.FromBase64String(chiffre.Split(':')[2]);
        bytes[^1] ^= 0x01;

        Should.Throw<CryptographicException>(() => Encryptor("a").Decrypt($"v1:a:{Convert.ToBase64String(bytes)}"));
    }

    [Fact]
    public void Changer_l_identifiant_de_cle_est_detecte()
    {
        var chiffre = Encryptor("a").Encrypt("secret");

        Should.Throw<CryptographicException>(() => Encryptor("a").Decrypt(chiffre.Replace("v1:a:", "v1:b:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Apres_rotation_les_anciennes_valeurs_restent_lisibles()
    {
        var ancien = Encryptor("a").Encrypt("dossier");
        var apresRotation = Encryptor("b");

        apresRotation.Decrypt(ancien).ShouldBe("dossier");
        apresRotation.NeedsRotation(ancien).ShouldBeTrue();
        apresRotation.NeedsRotation(apresRotation.Encrypt("dossier")).ShouldBeFalse();
    }

    [Fact]
    public void Une_cle_de_mauvaise_taille_est_refusee() =>
        Should.Throw<InvalidOperationException>(() => new ConfigurationFieldKeyProvider(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Encryption:CurrentKeyId"] = "a",
                ["Encryption:Keys:a"] = Convert.ToBase64String(new byte[16]),
            }).Build()));

    [Fact]
    public void L_index_aveugle_est_stable_normalise_et_depend_de_la_cle()
    {
        var index = new BlindIndex(Convert.FromBase64String(KeyA));

        index.Compute("85.07.30-033.28").ShouldBe(index.Compute("85073003328"));
        index.Compute("85073003328").ShouldNotBe(new BlindIndex(Convert.FromBase64String(KeyB)).Compute("85073003328"));
    }
}
