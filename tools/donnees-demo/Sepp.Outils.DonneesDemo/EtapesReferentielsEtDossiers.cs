using System.Globalization;
using System.Text.Json.Nodes;

namespace Sepp.Outils.DonneesDemo;

/// <summary>Étape 1 : référentiels (paramètres légaux présents, import du jeu d'exemple des risques).</summary>
internal static class EtapeReferentiels
{
    public static async Task ExecuterAsync(Contexte c)
    {
        c.Titre("1. Référentiels");
        var parametres = Api.Exiger(await c.Api.GetAsync(Service.Referentiels, "parametres-legaux", "admin"), "Lecture des paramètres légaux");
        c.Info($"{parametres.Liste().Count} paramètres légaux disponibles");

        var csv = Path.Combine(c.Options.Racine, "src", "Services", "PostesRisques", "exemples", "risques-exemple.csv");
        if (!File.Exists(csv))
        {
            throw new EchecDemoException($"Jeu d'exemple des risques introuvable : {csv}");
        }

        var import = await c.Api.PostTexteAsync(
            Service.PostesRisques, "risques/import?valideDu=2026-01-01", "admin", await File.ReadAllTextAsync(csv), "text/csv");
        Api.Exiger(import, "Import du jeu d'exemple des risques");
        c.Info("risques importés (import idempotent)");
    }
}

/// <summary>Étape 2 : affiliés fictifs avec unité d'établissement, site et un groupe.</summary>
internal static class EtapeAffilies
{
    public static async Task ExecuterAsync(Contexte c)
    {
        c.Titre("2. Affiliés");
        var groupeId = await AssurerGroupeAsync(c);
        foreach (var def in Catalogue.Affilies)
        {
            await c.EssayerAsync($"Affilié {def.Denomination}", async () =>
            {
                var id = await AssurerAffilieAsync(c, def);
                c.Affilies[def.Cle] = id;
                c.Info($"{def.Denomination} : BCE {def.Bce}, id {id}");
                await AssurerSiteAsync(c, def, id);
                if (def.Cle != "zorg")
                {
                    await RattacherAuGroupeAsync(c, def, id, groupeId);
                }
            });
        }
    }

    private static async Task<Guid?> AssurerGroupeAsync(Contexte c)
    {
        try
        {
            var existants = Api.Exiger(await c.Api.GetAsync(Service.Affilies, "groupes", "gestionnaire"), "Liste des groupes").Liste();
            var trouve = existants.FirstOrDefault(g => g.Texte("nom") == Catalogue.GroupeNom);
            if (trouve is not null)
            {
                return trouve.Guid("id");
            }

            var cree = Api.Exiger(await c.Api.PostAsync(Service.Affilies, "groupes", "gestionnaire", new { nom = Catalogue.GroupeNom }), "Création du groupe");
            return IdDe(cree);
        }
        catch (EchecDemoException ex)
        {
            c.Avertir($"Groupe : {ex.Message}");
            return null;
        }
    }

    private static async Task<Guid> AssurerAffilieAsync(Contexte c, AffilieDef def)
    {
        var recherche = Api.Exiger(await c.Api.GetAsync(Service.Affilies, $"affilies?bce={def.Bce}", "gestionnaire"), "Recherche de l'affilié par BCE");
        var trouve = recherche.Liste().FirstOrDefault(a => Chiffres(a.Texte("numeroBce")) == def.Bce);
        if (trouve is not null)
        {
            return trouve.Guid("id");
        }

        var corps = new
        {
            numeroBce = def.Bce,
            fiche = FicheDe(def),
            dateAffiliation = c.Aujourdhui.AddYears(-3),
        };
        return IdDe(Api.Exiger(await c.Api.PostAsync(Service.Affilies, "affilies", "gestionnaire", corps), $"Création de {def.Denomination}"));
    }

    private static object FicheDe(AffilieDef def) => new
    {
        denomination = def.Denomination,
        formeJuridique = def.FormeJuridique,
        codeNace = def.CodeNace,
        commissionParitaire = def.CommissionParitaire,
        categorieTarifaire = def.Categorie,
        langue = def.Langue,
        regimeLinguistique = def.Regime,
    };

    private static async Task AssurerSiteAsync(Contexte c, AffilieDef def, Guid affilieId)
    {
        var fiche = Api.Exiger(await c.Api.GetAsync(Service.Affilies, $"affilies/{affilieId}", "gestionnaire"), "Lecture de la fiche affilié");
        var unites = fiche.Corps?["unitesEtablissement"] as JsonArray ?? [];
        var adresse = new { rue = def.Rue, numero = def.Numero, boite = (string?)null, codePostal = def.CodePostal, localite = def.Localite, codePays = "BE" };
        var depuis = c.Aujourdhui.AddYears(-3);

        Guid uniteId;
        if (unites.FirstOrDefault() is { } unite)
        {
            uniteId = unite.Guid("id");
        }
        else
        {
            var cree = await c.Api.PostAsync(
                Service.Affilies, $"affilies/{affilieId}/unites-etablissement", "gestionnaire",
                new { numero = def.UniteEtablissement, nom = $"{def.Denomination} - siège", adresse, langue = def.Langue, depuis });
            uniteId = IdDe(Api.Exiger(cree, "Ajout de l'unité d'établissement"));
        }

        var sites = (unites.FirstOrDefault()?["sites"] as JsonArray) ?? [];
        if (sites.FirstOrDefault() is { } site)
        {
            c.Sites[def.Cle] = site.Guid("id");
            return;
        }

        var siteCree = await c.Api.PostAsync(
            Service.Affilies, $"affilies/{affilieId}/unites-etablissement/{uniteId}/sites", "gestionnaire",
            new { nom = def.SiteNom, adresse, latitude = (double?)null, longitude = (double?)null, depuis });
        c.Sites[def.Cle] = IdDe(Api.Exiger(siteCree, "Ajout du site"));
    }

    private static async Task RattacherAuGroupeAsync(Contexte c, AffilieDef def, Guid affilieId, Guid? groupeId)
    {
        if (groupeId is null)
        {
            return;
        }

        var fiche = Api.Exiger(await c.Api.GetAsync(Service.Affilies, $"affilies/{affilieId}", "gestionnaire"), "Lecture de la fiche affilié");
        if (fiche.Corps?["fiche"]?["groupeId"] is not null)
        {
            return;
        }

        Api.Exiger(
            await c.Api.PutAsync(Service.Affilies, $"affilies/{affilieId}", "gestionnaire", new { fiche = FicheDe(def), groupeId }),
            "Rattachement au groupe");
        c.Info($"{def.Denomination} rattaché au groupe « {Catalogue.GroupeNom} »");
    }

    /// <summary>Identifiant d'une réponse de création : objet <c>{ id }</c> ou GUID nu.</summary>
    public static Guid IdDe(Reponse reponse)
    {
        if (reponse.Corps is JsonObject o)
        {
            foreach (var cle in new[] { "id", "personneId" })
            {
                if (o[cle] is { } valeur)
                {
                    return Guid.Parse(valeur.GetValue<string>(), CultureInfo.InvariantCulture);
                }
            }
        }

        if (reponse.Corps is JsonValue v && v.TryGetValue<string>(out var texte) && Guid.TryParse(texte, out var id))
        {
            return id;
        }

        throw new EchecDemoException($"Identifiant absent de la réponse : {reponse.Texte}");
    }

    public static string Chiffres(string? texte) => new((texte ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
}

/// <summary>Étape 3 : postes (4 à 6 par affilié) et profils de risques validés par le CPMT, avec une proposition refusée.</summary>
internal static class EtapePostes
{
    private const string MotifProposition = "Démo : analyse de risques initiale";
    private const string MotifRefus = "Démo : proposition refusée, avis du comité PPT manquant";

    public static async Task ExecuterAsync(Contexte c)
    {
        c.Titre("3. Postes et profils de risques");
        foreach (var def in Catalogue.Affilies)
        {
            if (!c.Affilies.TryGetValue(def.Cle, out var affilieId))
            {
                c.Avertir($"Postes de {def.Denomination} ignorés : affilié absent.");
                continue;
            }

            var existants = Api.Exiger(await c.Api.GetAsync(Service.PostesRisques, $"postes?affilieId={affilieId}", "gestionnaire"), "Liste des postes").Liste();
            foreach (var poste in def.Postes)
            {
                await c.EssayerAsync($"Poste {poste.Intitule} ({def.Denomination})", async () =>
                {
                    var trouve = existants.FirstOrDefault(p => p.Texte("intitule") == poste.Intitule);
                    Guid id;
                    if (trouve is not null)
                    {
                        id = trouve.Guid("id");
                    }
                    else
                    {
                        var cree = await c.Api.PostAsync(
                            Service.PostesRisques, "postes", "gestionnaire",
                            new { affilieId, intitule = poste.Intitule, description = poste.Description, metierTypeCode = (string?)null });
                        id = EtapeAffilies.IdDe(Api.Exiger(cree, "Création du poste"));
                    }

                    c.Postes[$"{def.Cle}|{poste.Intitule}"] = id;
                    if (poste.Risques.Count > 0)
                    {
                        await AssurerProfilAsync(c, id, poste);
                    }
                });
            }

            c.Info($"{def.Denomination} : {def.Postes.Count} postes");
        }

        await c.EssayerAsync("Proposition refusée", () => AssurerPropositionRefuseeAsync(c));
    }

    private static async Task AssurerProfilAsync(Contexte c, Guid posteId, PosteDef poste)
    {
        var propositions = Api.Exiger(await c.Api.GetAsync(Service.PostesRisques, $"propositions-poste-risque?posteId={posteId}", "cpmt"), "Liste des propositions").Liste();
        if (propositions.Any(p => p.Texte("statut") == "Validee"))
        {
            return;
        }

        var lignes = poste.Risques.Select(r => new { type = "Ajout", risqueCode = r.Code, niveauExposition = r.Niveau }).ToList();
        var soumise = await c.Api.PostAsync(
            Service.PostesRisques, $"postes/{posteId}/propositions", "gestionnaire",
            new { motif = MotifProposition, valideDu = c.DebutProfils, lignes, avisCppt = (object?)null });
        var id = EtapeAffilies.IdDe(Api.Exiger(soumise, $"Proposition de profil pour {poste.Intitule}"));
        Api.Exiger(
            await c.Api.PostAsync(Service.PostesRisques, $"propositions-poste-risque/{id}/validation", "cpmt", new { }),
            $"Validation du profil de {poste.Intitule}");
    }

    private static async Task AssurerPropositionRefuseeAsync(Contexte c)
    {
        if (!c.Postes.TryGetValue("construct|Conducteur de travaux", out var posteId))
        {
            return;
        }

        var existantes = Api.Exiger(await c.Api.GetAsync(Service.PostesRisques, $"propositions-poste-risque?posteId={posteId}", "cpmt"), "Liste des propositions").Liste();
        if (existantes.Any(p => p.Texte("statut") == "Refusee"))
        {
            return;
        }

        var soumise = await c.Api.PostAsync(
            Service.PostesRisques, $"postes/{posteId}/propositions", "gestionnaire",
            new
            {
                motif = "Démo : ajout du risque de conduite",
                valideDu = c.DebutProfils.AddDays(30),
                lignes = new[] { new { type = "Ajout", risqueCode = "EX.SECU.CONDUITE", niveauExposition = "Moyen" } },
                avisCppt = (object?)null,
            });
        var id = EtapeAffilies.IdDe(Api.Exiger(soumise, "Proposition à refuser"));
        Api.Exiger(await c.Api.PostAsync(Service.PostesRisques, $"propositions-poste-risque/{id}/refus", "cpmt", new { motif = MotifRefus }), "Refus de la proposition");
        c.Info("une proposition refusée par le CPMT");
    }
}

/// <summary>Étape 4 : une quarantaine de travailleurs (FR, NL, DE) avec occupation et affectation à un poste.</summary>
internal static class EtapePersonnes
{
    public static async Task ExecuterAsync(Contexte c)
    {
        c.Titre("4. Personnes, occupations et affectations");
        var definitions = Catalogue.Personnes(c.Aujourdhui);
        foreach (var def in definitions)
        {
            if (!c.Affilies.TryGetValue(def.AffilieCle, out var affilieId) || !c.Sites.TryGetValue(def.AffilieCle, out var siteId)
                || !c.Postes.TryGetValue($"{def.AffilieCle}|{def.PosteIntitule}", out var posteId))
            {
                c.Avertir($"Personne {def.NomComplet} ignorée : affilié, site ou poste absent.");
                continue;
            }

            await c.EssayerAsync($"Personne {def.NomComplet}", async () =>
            {
                var personne = new PersonneDemo(def) { AffilieId = affilieId };
                await AssurerAsync(c, personne, siteId, posteId);
                c.Personnes[def.Cle] = personne;
            });
        }

        c.Info($"{c.Personnes.Count} travailleurs ({definitions.Count(d => d.Langue == "Fr")} FR, {definitions.Count(d => d.Langue == "Nl")} NL, {definitions.Count(d => d.Langue == "De")} DE)");
        await c.EssayerAsync("État particulier", async () => await AssurerEtatParticulierAsync(c));
    }

    private static async Task AssurerAsync(Contexte c, PersonneDemo p, Guid siteId, Guid posteId)
    {
        var def = p.Def;
        var debut = c.Aujourdhui.AddDays(-def.JoursDepuisDebut);
        var recherche = await c.Api.PostAsync(Service.Personnes, "personnes/recherche", "gestionnaire", new { niss = def.Niss });
        if (recherche.Statut == System.Net.HttpStatusCode.NotFound)
        {
            var corps = new
            {
                niss = def.Niss,
                identite = new { nom = def.Nom, prenom = def.Prenom, dateNaissance = def.Naissance, sexe = def.Sexe, langue = def.Langue },
                coordonnees = new
                {
                    adresse = new { rue = def.Rue, numero = def.Numero, boite = (string?)null, codePostal = def.CodePostal, localite = def.Localite, pays = "BE" },
                    email = def.Email,
                    telephone = (string?)null,
                    canalPrefere = def.Canal,
                },
                occupation = new
                {
                    affilieId = p.AffilieId,
                    affilieUtilisateurId = (Guid?)null,
                    typeTravailleur = def.TypeTravailleur,
                    typeContrat = def.TypeContrat,
                    dateDebut = debut,
                    dateFin = (DateOnly?)null,
                },
            };
            var cree = Api.Exiger(await c.Api.PostAsync(Service.Personnes, "personnes", "gestionnaire", corps), $"Création de {def.NomComplet}");
            p.Id = cree.Corps.Guid("personneId");
            p.OccupationId = cree.Corps.Guid("occupationId");
        }
        else
        {
            Api.Exiger(recherche, $"Recherche de {def.NomComplet} par NISS");
            p.Id = recherche.Corps.Guid("id");
        }

        var fiche = Api.Exiger(await c.Api.GetAsync(Service.Personnes, $"personnes/{p.Id}", "gestionnaire"), "Lecture de la personne");
        var occupations = fiche.Corps?["occupations"] as JsonArray ?? [];
        var occupation = occupations.FirstOrDefault(o => o.Texte("affilieId") == p.AffilieId.ToString());
        if (occupation is null)
        {
            var corps = new
            {
                affilieId = p.AffilieId,
                affilieUtilisateurId = (Guid?)null,
                typeTravailleur = def.TypeTravailleur,
                typeContrat = def.TypeContrat,
                dateDebut = debut,
                dateFin = (DateOnly?)null,
            };
            p.OccupationId = EtapeAffilies.IdDe(Api.Exiger(await c.Api.PostAsync(Service.Personnes, $"personnes/{p.Id}/occupations", "gestionnaire", corps), "Ajout de l'occupation"));
        }
        else
        {
            p.OccupationId = occupation.Guid("id");
        }

        var affectations = occupation?["affectations"] as JsonArray;
        if (occupation is null || affectations is null || affectations.Count == 0)
        {
            var affectation = new { posteId, siteId, valideDu = debut, valideJusquAu = (DateOnly?)null };
            Api.Exiger(
                await c.Api.PostAsync(Service.Personnes, $"personnes/{p.Id}/occupations/{p.OccupationId}/affectations", "gestionnaire", affectation),
                $"Affectation de {def.NomComplet}");
        }
    }

    private static async Task AssurerEtatParticulierAsync(Contexte c)
    {
        if (!c.Personnes.TryGetValue("boulangerie-13", out var p))
        {
            return;
        }

        var etats = Api.Exiger(await c.Api.GetAsync(Service.Personnes, $"personnes/{p.Id}/etats-particuliers", "gestionnaire"), "États particuliers").Liste();
        if (etats.Count > 0)
        {
            return;
        }

        var cree = await c.Api.PostAsync(
            Service.Personnes, $"personnes/{p.Id}/etats-particuliers", "gestionnaire",
            new { type = "TravailDeNuit", dateDebut = c.Aujourdhui.AddDays(-400), dateFin = (DateOnly?)null });
        Api.Exiger(cree, "État particulier « travail de nuit »");
        c.Info($"{p.Def.NomComplet} : état particulier « travail de nuit »");
    }
}
