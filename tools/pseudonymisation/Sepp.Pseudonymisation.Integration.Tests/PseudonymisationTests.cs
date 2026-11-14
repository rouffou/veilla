using System.Security.Cryptography;

using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.Personnes.Domain.Personnes;

using Shouldly;

namespace Sepp.Pseudonymisation.Integration.Tests;

public sealed class PseudonymisationTests(CopieDeBases copie) : IClassFixture<CopieDeBases>
{
    private static string Texte(object? valeur) => valeur as string ?? string.Empty;

    [Fact]
    public async Task Aucune_valeur_d_origine_ne_reste_dans_les_colonnes_ciblees()
    {
        await copie.ExecuterOutilAsync();

        var tablesLues = 0;
        foreach (var service in CatalogueRegles.Tous)
        {
            foreach (var table in service.Tables)
            {
                var colonnes = table.Colonnes.Where(c => c.Technique != Technique.Conserver).ToList();
                var lignes = await CopieDeBases.LireAsync(copie.Connexions[service.Nom], $"SELECT {string.Join(", ", colonnes.Select(c => $"\"{c.Colonne}\"::text"))} FROM \"{table.Table}\"");
                tablesLues += lignes.Count > 0 ? 1 : 0;
                foreach (var ligne in lignes)
                {
                    for (var i = 0; i < colonnes.Count; i++)
                    {
                        var valeur = Texte(ligne[i]);
                        var contexte = $"{service.Nom}.{table.Table}.{colonnes[i].Colonne}";
                        if (colonnes[i].Technique.ToString().StartsWith("Chiffre", StringComparison.Ordinal) || colonnes[i].Technique == Technique.NissChiffre)
                        {
                            if (ligne[i] is not null)
                            {
                                // Rechiffré par la clé cible : la clé source ne déchiffre plus rien.
                                copie.Cibles[service.Nom].Decrypt(valeur).ShouldNotContain(CopieDeBases.Marqueur, customMessage: contexte);
                                Should.Throw<CryptographicException>(() => copie.Sources[service.Nom].Decrypt(valeur), contexte);
                            }
                        }
                        else
                        {
                            valeur.ShouldNotContain(CopieDeBases.Marqueur, customMessage: contexte);
                        }
                    }
                }
            }
        }

        tablesLues.ShouldBeGreaterThanOrEqualTo(20);
    }

    [Fact]
    public async Task Les_colonnes_d_audit_et_les_files_d_evenements_sont_traitees_dans_tous_les_services()
    {
        await copie.ExecuterOutilAsync();

        foreach (var service in CatalogueRegles.Tous)
        {
            var cs = copie.Connexions[service.Nom];
            var tables = await CopieDeBases.LireAsync(cs, "SELECT table_name FROM information_schema.columns WHERE table_schema = 'public' AND column_name = 'created_by'");
            foreach (var table in tables.Select(t => (string)t[0]!))
            {
                var restes = await CopieDeBases.LireAsync(cs, $"SELECT count(*) FROM \"{table}\" WHERE created_by LIKE '%{CopieDeBases.Marqueur}%' OR updated_by LIKE '%{CopieDeBases.Marqueur}%'");
                ((long)restes[0][0]!).ShouldBe(0, $"{service.Nom}.{table}");
            }

            foreach (var file in new[] { "outbox_message", "inbox_message" })
            {
                ((long)(await CopieDeBases.LireAsync(cs, $"SELECT count(*) FROM \"{file}\""))[0][0]!).ShouldBe(0);
            }
        }
    }

    [Fact]
    public async Task Les_niss_fictifs_sont_valides_uniques_coherents_avec_la_naissance_et_l_index_aveugle_est_recalcule()
    {
        await copie.ExecuterOutilAsync();
        var chiffre = copie.Cibles[CatalogueRegles.Personnes];
        var index = new BlindIndex(copie.CleIndexCible);
        var lignes = await CopieDeBases.LireAsync(copie.Connexions[CatalogueRegles.Personnes], "SELECT id, niss_chiffre, niss_hash, date_naissance FROM personne");

        lignes.Count.ShouldBe(42);
        var tous = new HashSet<string>();
        foreach (var ligne in lignes)
        {
            var valeur = chiffre.Decrypt((string)ligne[1]!);
            Niss.TryParse(valeur, out var niss, out _).ShouldBeTrue();
            tous.Add(valeur).ShouldBeTrue("NISS fictif en double");
            valeur.ShouldNotBe(copie.NissP1);
            valeur.ShouldNotBe(copie.NissP2);
            ((string)ligne[2]!).ShouldBe(index.Compute(valeur));

            var naissance = Techniques.Date(ligne[3]);
            niss.DateNaissance().ShouldBe(naissance);
            naissance.ShouldNotBe(copie.NaissancesOriginales[(Guid)ligne[0]!]);
            Math.Abs(naissance.DayNumber - copie.NaissancesOriginales[(Guid)ligne[0]!].DayNumber).ShouldBeLessThanOrEqualTo(120);
        }
    }

    [Fact]
    public async Task Les_identifiants_de_personnes_restent_coherents_entre_les_services()
    {
        await copie.ExecuterOutilAsync();
        var ids = (await CopieDeBases.LireAsync(copie.Connexions[CatalogueRegles.Personnes], "SELECT id FROM personne")).Select(l => (Guid)l[0]!).ToHashSet();
        ids.ShouldBe(copie.PersonnesIds, ignoreOrder: true);

        foreach (var (service, sql) in new[]
                 {
                     (CatalogueRegles.SurveillanceMedicale, "SELECT personne_id FROM dossier_sante UNION ALL SELECT personne_id FROM examen UNION ALL SELECT personne_id FROM decision"),
                     (CatalogueRegles.Reintegration, "SELECT personne_id FROM incapacite UNION ALL SELECT personne_id FROM trajet"),
                 })
        {
            var references = (await CopieDeBases.LireAsync(copie.Connexions[service], sql)).Select(l => (Guid)l[0]!).ToList();
            references.ShouldNotBeEmpty();
            references.ShouldAllBe(r => ids.Contains(r), service);
        }
    }

    [Fact]
    public async Task Le_meme_numero_bce_donne_la_meme_valeur_fictive_valide_dans_tout_le_service()
    {
        await copie.ExecuterOutilAsync();
        var cs = copie.Connexions[CatalogueRegles.Affilies];
        var affilie = Texte((await CopieDeBases.LireAsync(cs, "SELECT numero_bce FROM affilie"))[0][0]);
        var ecart = Texte((await CopieDeBases.LireAsync(cs, "SELECT numero_bce FROM ecart_synchronisation"))[0][0]);

        ecart.ShouldBe(affilie);
        affilie.ShouldNotBe(copie.BceA1);
        (97 - (long.Parse(affilie[..8]) % 97)).ShouldBe(long.Parse(affilie[8..]));
    }

    [Fact]
    public async Task Les_valeurs_conservees_gardent_leur_sens_metier()
    {
        await copie.ExecuterOutilAsync();
        var etat = (string)(await CopieDeBases.LireAsync(copie.Connexions[CatalogueRegles.Personnes], "SELECT type_chiffre FROM etat_particulier"))[0][0]!;
        copie.Cibles[CatalogueRegles.Personnes].Decrypt(etat).ShouldBe("Grossesse");

        var json = (string)(await CopieDeBases.LireAsync(copie.Connexions[CatalogueRegles.SurveillanceMedicale], "SELECT valeurs_chiffre FROM resultat_acte"))[0][0]!;
        var clair = copie.Cibles[CatalogueRegles.SurveillanceMedicale].Decrypt(json);
        clair.ShouldContain("\"valeur\":1.5");
        clair.ShouldContain("\"code\":\"A\"");

        var lignes = await CopieDeBases.LireAsync(copie.Connexions[CatalogueRegles.Personnes], "SELECT adresse_code_postal, adresse_localite, sexe FROM personne WHERE sexe = 'Feminin'");
        Texte(lignes[0][0]).ShouldBe("1000");
    }

    [Fact]
    public async Task Le_rapport_liste_ce_qui_reste_sans_regle()
    {
        var rapports = await copie.ExecuterOutilAsync();

        var affilies = rapports.Single(r => r.Service == CatalogueRegles.Affilies);
        affilies.ColonnesNonCouvertes.ShouldContain("affilie.forme_juridique");
        affilies.ColonnesNonCouvertes.ShouldNotContain("affilie.denomination");
        affilies.ColonnesNonCouvertes.ShouldNotContain("contact.email");
        rapports.Single(r => r.Service == CatalogueRegles.Personnes).LignesParTable["personne"].ShouldBe(42);
    }

    [Fact]
    public async Task Une_seconde_execution_sur_la_meme_base_est_refusee()
    {
        await copie.ExecuterOutilAsync();

        var erreur = await Should.ThrowAsync<InvalidOperationException>(
            () => Execution.ExecuterAsync(copie.Configuration(services: CatalogueRegles.Affilies), TextWriter.Null, CancellationToken.None));

        erreur.Message.ShouldContain("déjà été pseudonymisée");
    }

    [Fact]
    public async Task Le_mode_a_blanc_n_ecrit_rien()
    {
        var connexion = await copie.CreerBaseVideAsync(CatalogueRegles.Affilies);
        await CopieDeBases.InsererAsync(connexion, "groupe", ("nom", $"{CopieDeBases.Marqueur} Groupe"));
        var regles = CatalogueRegles.Pour(CatalogueRegles.Affilies);

        var rapport = await Moteur.ExecuterAsync(regles, connexion, new Pseudonymiseur(RandomNumberGenerator.GetBytes(32)), null, null, null, "recette", aBlanc: true, TestContext.Current.CancellationToken);

        rapport.LignesParTable["groupe"].ShouldBe(1);
        Texte((await CopieDeBases.LireAsync(connexion, "SELECT nom FROM groupe"))[0][0]).ShouldContain(CopieDeBases.Marqueur);
        (await CopieDeBases.LireAsync(connexion, $"SELECT 1 FROM information_schema.tables WHERE table_name = '{Moteur.TableMarqueur}'")).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("prod", "prod")]
    [InlineData("production", "production")]
    [InlineData("recette", null)]
    public async Task L_outil_refuse_la_production_et_l_absence_de_confirmation(string environnement, string? confirmation)
    {
        await Should.ThrowAsync<InvalidOperationException>(
            () => Execution.ExecuterAsync(copie.Configuration(environnement, confirmation), TextWriter.Null, CancellationToken.None));
    }
}
