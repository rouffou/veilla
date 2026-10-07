using System.Security.Cryptography;

using Microsoft.Extensions.Configuration;

using Sepp.Documents.Adapters.Chiffrement;
using Sepp.Documents.Application;
using Sepp.Documents.Domain.Commun;

using Shouldly;

namespace Sepp.Documents.Integration.Tests;

/// <summary>§14.3, ARC-45 : contenu chiffré par zone avec des clés distinctes.</summary>
public class ChiffrementParZoneTests
{
    private static readonly byte[] Clair = "contenu confidentiel"u8.ToArray();

    private static Dictionary<string, string?> Cles(string? cleStandard = null, string? cleMedicale = null, string? clePsy = null) => new()
    {
        ["Documents:Chiffrement:standard:Encryption:CurrentKeyId"] = "std-1",
        ["Documents:Chiffrement:standard:Encryption:Keys:std-1"] = cleStandard ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        ["Documents:Chiffrement:medicale:Encryption:CurrentKeyId"] = "med-1",
        ["Documents:Chiffrement:medicale:Encryption:Keys:med-1"] = cleMedicale ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        ["Documents:Chiffrement:psychosociale:Encryption:CurrentKeyId"] = "psy-1",
        ["Documents:Chiffrement:psychosociale:Encryption:Keys:psy-1"] = clePsy ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
    };

    private static ChiffrementParZone Chiffrement(Dictionary<string, string?> valeurs) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(valeurs).Build());

    [Fact]
    public void Le_contenu_chiffre_se_dechiffre_avec_la_cle_de_sa_zone()
    {
        var chiffrement = Chiffrement(Cles());
        var id = Guid.CreateVersion7();

        var chiffre = chiffrement.Chiffrer(ZoneDocument.Medicale, id, Clair);

        chiffre.CleId.ShouldBe("med-1");
        System.Text.Encoding.UTF8.GetString(chiffre.Octets).ShouldNotContain("confidentiel");
        chiffrement.Dechiffrer(ZoneDocument.Medicale, id, chiffre.Octets).ShouldBe(Clair);
    }

    [Fact]
    public void Chaque_zone_utilise_sa_propre_cle()
    {
        var chiffrement = Chiffrement(Cles());
        var id = Guid.CreateVersion7();

        chiffrement.Chiffrer(ZoneDocument.Standard, id, Clair).CleId.ShouldBe("std-1");
        chiffrement.Chiffrer(ZoneDocument.Medicale, id, Clair).CleId.ShouldBe("med-1");
        chiffrement.Chiffrer(ZoneDocument.Psychosociale, id, Clair).CleId.ShouldBe("psy-1");
    }

    [Fact]
    public void Un_contenu_ne_se_dechiffre_ni_dans_une_autre_zone_ni_pour_un_autre_document()
    {
        var chiffrement = Chiffrement(Cles());
        var id = Guid.CreateVersion7();
        var chiffre = chiffrement.Chiffrer(ZoneDocument.Medicale, id, Clair).Octets;

        Should.Throw<ContenuAltereException>(() => chiffrement.Dechiffrer(ZoneDocument.Standard, id, chiffre));
        Should.Throw<ContenuAltereException>(() => chiffrement.Dechiffrer(ZoneDocument.Psychosociale, id, chiffre));
        Should.Throw<ContenuAltereException>(() => chiffrement.Dechiffrer(ZoneDocument.Medicale, Guid.CreateVersion7(), chiffre));
    }

    [Fact]
    public void Un_octet_modifie_ou_un_contenu_tronque_est_detecte()
    {
        var chiffrement = Chiffrement(Cles());
        var id = Guid.CreateVersion7();
        var chiffre = chiffrement.Chiffrer(ZoneDocument.Standard, id, Clair).Octets;

        var altere = (byte[])chiffre.Clone();
        altere[^1] ^= 0x01;

        Should.Throw<ContenuAltereException>(() => chiffrement.Dechiffrer(ZoneDocument.Standard, id, altere));
        Should.Throw<ContenuAltereException>(() => chiffrement.Dechiffrer(ZoneDocument.Standard, id, chiffre[..20]));
        Should.Throw<ContenuAltereException>(() => chiffrement.Dechiffrer(ZoneDocument.Standard, id, [1, 2, 3]));
    }

    [Fact]
    public void Deux_chiffrements_du_meme_contenu_different()
    {
        var chiffrement = Chiffrement(Cles());
        var id = Guid.CreateVersion7();

        chiffrement.Chiffrer(ZoneDocument.Standard, id, Clair).Octets.ShouldNotBe(chiffrement.Chiffrer(ZoneDocument.Standard, id, Clair).Octets);
    }

    [Fact]
    public void Une_meme_cle_configuree_pour_deux_zones_est_refusee_au_demarrage()
    {
        var partagee = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        Should.Throw<InvalidOperationException>(() => Chiffrement(Cles(cleStandard: partagee, cleMedicale: partagee)))
            .Message.ShouldContain("une clé par zone");
    }

    [Fact]
    public void Une_zone_sans_cle_est_refusee_au_demarrage()
    {
        var valeurs = Cles();
        valeurs.Remove("Documents:Chiffrement:psychosociale:Encryption:CurrentKeyId");

        Should.Throw<InvalidOperationException>(() => Chiffrement(valeurs));
    }

    [Fact]
    public void La_rotation_des_cles_conserve_le_dechiffrement_des_anciens_documents()
    {
        var ancienne = Cles();
        var id = Guid.CreateVersion7();
        var chiffre = Chiffrement(ancienne).Chiffrer(ZoneDocument.Standard, id, Clair).Octets;

        var rotation = new Dictionary<string, string?>(ancienne)
        {
            ["Documents:Chiffrement:standard:Encryption:CurrentKeyId"] = "std-2",
            ["Documents:Chiffrement:standard:Encryption:Keys:std-2"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        };
        var chiffrement = Chiffrement(rotation);

        chiffrement.Dechiffrer(ZoneDocument.Standard, id, chiffre).ShouldBe(Clair);
        chiffrement.Chiffrer(ZoneDocument.Standard, id, Clair).CleId.ShouldBe("std-2");
    }
}
