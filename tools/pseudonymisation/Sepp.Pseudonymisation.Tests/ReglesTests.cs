using System.Security.Cryptography;

using Sepp.BuildingBlocks.Infrastructure.Security;

using Shouldly;

namespace Sepp.Pseudonymisation.Tests;

public sealed class PseudonymiseurTests
{
    private static Pseudonymiseur Avec(byte octet = 1) => new(Enumerable.Repeat(octet, 32).ToArray());

    [Fact]
    public void Une_cle_trop_courte_est_refusee()
    {
        Should.Throw<ArgumentException>(() => new Pseudonymiseur(new byte[16]));
    }

    [Fact]
    public void Les_valeurs_fictives_sont_deterministes_pour_une_meme_cle_et_changent_avec_la_cle()
    {
        var a = Avec(1);
        var b = Avec(1);
        var autre = Avec(2);

        a.NomComplet("id-1").ShouldBe(b.NomComplet("id-1"));
        a.Email("id-1").ShouldBe(b.Email("id-1"));
        a.Utilisateur("jean@exemple.be").ShouldBe(b.Utilisateur("jean@exemple.be"));
        a.Utilisateur("jean@exemple.be").ShouldNotBe(autre.Utilisateur("jean@exemple.be"));
        a.Utilisateur("jean@exemple.be").ShouldNotContain("jean");
        a.Utilisateur("system").ShouldBe("system");
    }

    [Fact]
    public void Le_decalage_de_date_est_non_nul_borne_et_stable()
    {
        var p = Avec();
        for (var i = 0; i < 500; i++)
        {
            var decalage = p.DecalageJours($"personne-{i}");
            decalage.ShouldNotBe(0);
            Math.Abs(decalage).ShouldBeLessThanOrEqualTo(120);
            p.DecalageJours($"personne-{i}").ShouldBe(decalage);
        }
    }

    [Theory]
    [InlineData("Masculin", 1985, 7, 30)]
    [InlineData("Feminin", 2003, 1, 2)]
    [InlineData("Inconnu", 1999, 12, 31)]
    public void Le_niss_fictif_est_valide_coherent_avec_la_date_et_le_sexe(string sexe, int annee, int mois, int jour)
    {
        var p = Avec();
        var niss = p.Niss("original", new DateOnly(annee, mois, jour), sexe);

        niss.Length.ShouldBe(11);
        niss[..6].ShouldBe($"{annee % 100:D2}{mois:D2}{jour:D2}");
        var corps = long.Parse(niss[..9]) + (annee >= 2000 ? 2_000_000_000L : 0);
        (97 - (corps % 97)).ShouldBe(long.Parse(niss[9..]));
        if (sexe == "Masculin")
        {
            (int.Parse(niss[6..9]) % 2).ShouldBe(1);
        }
        else if (sexe == "Feminin")
        {
            (int.Parse(niss[6..9]) % 2).ShouldBe(0);
        }

    }

    [Fact]
    public void Les_niss_fictifs_sont_uniques_meme_pour_beaucoup_de_personnes_nees_le_meme_jour()
    {
        var p = Avec();
        var valeurs = Enumerable.Range(0, 400).Select(i => p.Niss($"niss-{i}", new DateOnly(1990, 5, 5), "Masculin")).ToList();

        valeurs.Distinct().Count().ShouldBe(400);
        p.Niss("niss-7", new DateOnly(1990, 5, 5), "Masculin").ShouldBe(valeurs[7]);
    }

    [Fact]
    public void Les_numeros_bce_fictifs_sont_valides_uniques_et_stables()
    {
        var p = Avec();
        var bce = Enumerable.Range(0, 2000).Select(i => p.NumeroBce($"0{i:D9}"[..10])).ToList();

        bce.Distinct().Count().ShouldBe(2000);
        foreach (var numero in bce)
        {
            numero.Length.ShouldBe(10);
            (numero[0] is '0' or '1').ShouldBeTrue();
            (97 - (long.Parse(numero[..8]) % 97)).ShouldBe(long.Parse(numero[8..]));
        }

        p.NumeroBce("0000000007").ShouldBe(bce[7]);
        var ue = p.NumeroUniteEtablissement("2000000001");
        (ue[0] is >= '2' and <= '8').ShouldBeTrue();
        (97 - (long.Parse(ue[..8]) % 97)).ShouldBe(long.Parse(ue[8..]));
    }

    [Fact]
    public void Les_valeurs_fictives_respectent_les_longueurs_des_colonnes()
    {
        var p = Avec();
        for (var i = 0; i < 200; i++)
        {
            var seed = Guid.NewGuid().ToString();
            p.Nom(seed).Length.ShouldBeLessThanOrEqualTo(100);
            p.Email(seed).Length.ShouldBeLessThanOrEqualTo(254);
            p.Telephone(seed).Length.ShouldBeLessThanOrEqualTo(30);
            p.Rue(seed).Length.ShouldBeLessThanOrEqualTo(200);
            p.Denomination(seed).Length.ShouldBeLessThanOrEqualTo(200);
            p.Email(seed).ShouldEndWith("@exemple.invalid");
        }
    }
}

public sealed class TechniquesTests
{
    private sealed class Cles(string id, byte[] cle) : IFieldKeyProvider
    {
        public string CurrentKeyId => id;

        public byte[] GetKey(string keyId) => cle;
    }

    private static FieldEncryptor Chiffreur(string id) => new(new Cles(id, RandomNumberGenerator.GetBytes(32)));

    private static ContexteLigne Contexte(Dictionary<string, object?> originales, FieldEncryptor? source = null, FieldEncryptor? cible = null) =>
        new("t", Guid.NewGuid(), originales, new Pseudonymiseur(new byte[32]), source, cible, new BlindIndex(RandomNumberGenerator.GetBytes(32)));

    [Fact]
    public void Le_json_neutralise_les_chaines_des_proprietes_nommees_et_garde_le_reste()
    {
        const string json = """[{"codeQuestion":"Q1","valeur":"Mal de dos"},{"code":"A","valeur":12.5,"texte":"RAS","inhabituelle":true}]""";

        var resultat = Techniques.NeutraliserJson(json, "valeur,texte");

        resultat.ShouldNotContain("Mal de dos");
        resultat.ShouldNotContain("RAS");
        resultat.ShouldContain("Q1");
        resultat.ShouldContain("12.5");
        resultat.ShouldContain("true");
    }

    [Fact]
    public void Un_champ_chiffre_est_rechiffre_par_la_cle_cible_sans_changer_la_valeur()
    {
        var source = Chiffreur("src");
        var cible = Chiffreur("cible");
        var contexte = Contexte(new() { ["c"] = source.Encrypt("Grossesse") }, source, cible);

        var nouveau = (string)Techniques.Appliquer(new RegleColonne("c", Technique.ChiffreRechiffre), contexte)!;

        nouveau.ShouldStartWith("v2:cible:");
        cible.Decrypt(nouveau).ShouldBe("Grossesse");
    }

    [Fact]
    public void Un_texte_chiffre_supprime_ne_contient_plus_l_original_et_null_reste_null()
    {
        var source = Chiffreur("src");
        var cible = Chiffreur("cible");

        var nouveau = (string)Techniques.Appliquer(new RegleColonne("c", Technique.ChiffreSupprime), Contexte(new() { ["c"] = source.Encrypt("secret médical") }, source, cible))!;
        var nul = Techniques.Appliquer(new RegleColonne("c", Technique.ChiffreSupprime), Contexte(new() { ["c"] = null }, source, cible));

        cible.Decrypt(nouveau).ShouldBe(Pseudonymiseur.TexteSupprime);
        nul.ShouldBeNull();
    }

    [Fact]
    public void L_index_aveugle_est_calcule_sur_le_niss_fictif_avec_la_cle_cible()
    {
        var source = Chiffreur("src");
        var cible = Chiffreur("cible");
        var cle = RandomNumberGenerator.GetBytes(32);
        var contexte = new ContexteLigne("personne", Guid.NewGuid(),
            new Dictionary<string, object?> { ["niss_chiffre"] = source.Encrypt("85073003328"), ["date_naissance"] = new DateOnly(1985, 7, 30), ["sexe"] = "Masculin" },
            new Pseudonymiseur(new byte[32]), source, cible, new BlindIndex(cle));

        var chiffre = (string)Techniques.Appliquer(new RegleColonne("niss_chiffre", Technique.NissChiffre), contexte)!;
        var hash = (string)Techniques.Appliquer(new RegleColonne("niss_hash", Technique.NissIndexAveugle), contexte)!;

        var fictif = cible.Decrypt(chiffre);
        fictif.ShouldNotBe("85073003328");
        hash.ShouldBe(new BlindIndex(cle).Compute(fictif));
    }

    [Fact]
    public void Le_catalogue_declare_les_quatre_services_sans_doublon_et_avec_des_techniques_coherentes()
    {
        CatalogueRegles.Tous.Select(s => s.Nom).ShouldBe([CatalogueRegles.Personnes, CatalogueRegles.Affilies, CatalogueRegles.SurveillanceMedicale, CatalogueRegles.Reintegration]);
        foreach (var service in CatalogueRegles.Tous)
        {
            service.Tables.Select(t => t.Table).Distinct().Count().ShouldBe(service.Tables.Count);
            foreach (var table in service.Tables)
            {
                table.Colonnes.Select(c => c.Colonne).Distinct().Count().ShouldBe(table.Colonnes.Count, table.Table);
                if (table.Colonnes.Any(c => c.Technique is Technique.ChiffreJson))
                {
                    table.Colonnes.Where(c => c.Technique == Technique.ChiffreJson).ShouldAllBe(c => !string.IsNullOrEmpty(c.Parametre));
                }
            }

            if (service.Tables.Any(t => t.Colonnes.Any(c => c.Technique.ToString().StartsWith("Chiffre", StringComparison.Ordinal) || c.Technique == Technique.NissChiffre)))
            {
                service.Chiffrement.ShouldBeTrue(service.Nom);
            }
        }

        Should.Throw<InvalidOperationException>(() => CatalogueRegles.Pour("inconnu"));
    }
}

public sealed class GardeCibleTests
{
    private static GardeCible.Cible[] Cibles(string hote = "bdd-recette.postgres.example", string nomBase = "personnes") =>
        [new("personnes", $"Host={hote};Database={nomBase};Username=u;Password=x")];

    [Fact]
    public void Une_cible_de_recette_confirmee_est_acceptee()
    {
        Should.NotThrow(() => GardeCible.Verifier("recette", Cibles(), "recette"));
    }

    [Theory]
    [InlineData("prod")]
    [InlineData("Production")]
    [InlineData("PRD")]
    public void Un_environnement_de_production_est_refuse_meme_confirme(string environnement)
    {
        Should.Throw<InvalidOperationException>(() => GardeCible.Verifier(environnement, Cibles(), environnement));
    }

    [Fact]
    public void L_environnement_du_processus_en_production_est_refuse()
    {
        Should.Throw<InvalidOperationException>(() => GardeCible.Verifier("recette", Cibles(), "recette", environnementProcessus: "prod"));
    }

    [Theory]
    [InlineData("sepp-prod.postgres.example", "personnes")]
    [InlineData("bdd.postgres.example", "personnes_production")]
    [InlineData("prd-pg.example", "personnes")]
    public void Un_hote_ou_une_base_de_production_est_refuse(string hote, string nomBase)
    {
        var erreur = Should.Throw<InvalidOperationException>(() => GardeCible.Verifier("recette", Cibles(hote, nomBase), "recette"));
        erreur.Message.ShouldContain("production");
    }

    [Fact]
    public void La_preproduction_n_est_pas_confondue_avec_la_production()
    {
        Should.NotThrow(() => GardeCible.Verifier("preprod", Cibles("bdd-preprod.example", "personnes_preprod"), "preprod"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("formation")]
    public void Sans_confirmation_exacte_l_outil_refuse(string? confirmation)
    {
        Should.Throw<InvalidOperationException>(() => GardeCible.Verifier("recette", Cibles(), confirmation));
    }

    [Fact]
    public void Les_listes_d_hotes_interdits_et_autorises_sont_appliquees()
    {
        Should.Throw<InvalidOperationException>(() => GardeCible.Verifier("recette", Cibles("bdd.interdit.example"), "recette", hotesInterdits: ["*.interdit.example"]));
        Should.Throw<InvalidOperationException>(() => GardeCible.Verifier("recette", Cibles("bdd.autre.example"), "recette", hotesAutorises: ["*.recette.example"]));
        Should.NotThrow(() => GardeCible.Verifier("recette", Cibles("bdd.recette.example"), "recette", hotesAutorises: ["*.recette.example"]));
    }
}
