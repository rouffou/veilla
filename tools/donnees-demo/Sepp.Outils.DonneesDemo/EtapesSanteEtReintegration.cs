using System.Globalization;
using System.Text.Json.Nodes;

namespace Sepp.Outils.DonneesDemo;

/// <summary>Décision à rédiger : catégorie, mesures codées et validité (jamais de donnée médicale détaillée dans l'outil au-delà de textes fictifs).</summary>
internal sealed record DecisionSpec(string Categorie, string[] Mesures, int? ValiditeJours);

/// <summary>
/// Étape 6 : dossiers de santé (zone médicale), examens clôturés avec décisions signées (documents FR et NL générés par Documents),
/// vaccinations avec rappel dû, un examen en cours et une décision en brouillon à signer en direct.
/// </summary>
internal static class EtapeSurveillance
{
    private const string UtilisateurMedical = "cpmt";

    public static async Task ExecuterAsync(Contexte c)
    {
        c.Titre("6. Surveillance médicale");
        var sante = new JsonObject();
        c.Manifeste["sante"] = sante;
        var cpmtSub = await c.Jetons.SubAsync(UtilisateurMedical);

        // Dossiers : travailleurs avec rendez-vous du jour, décisions, vaccinations, examen en cours et brouillon.
        var avecDossier = new[]
        {
            "boulangerie-0", "boulangerie-1", "boulangerie-2", "construct-0", "construct-1",
            "boulangerie-9", "boulangerie-10", "boulangerie-11", "construct-6", "construct-7", "construct-8",
            "zorg-6", "zorg-7", "zorg-8", "zorg-9", "zorg-10",
        };
        foreach (var cle in avecDossier)
        {
            await c.EssayerAsync($"Dossier de {cle}", async () =>
            {
                if (c.Personnes.TryGetValue(cle, out var p))
                {
                    p.DossierId = await AssurerDossierAsync(c, p, cpmtSub);
                }
            });
        }

        c.Info($"{c.Personnes.Values.Count(p => p.DossierId is not null)} dossiers de santé");

        var signees = new JsonArray();
        sante["decisionsSignees"] = signees;
        var specs = new (string Cle, string Type, int JoursAvant, DecisionSpec Decision)[]
        {
            ("boulangerie-0", "EVALUATION_PREALABLE", 330, new("Apte", [], null)),
            ("boulangerie-9", "EVALUATION_PERIODIQUE", 90, new("ApteAvecMesures", ["AMENAGEMENT_POSTE"], null)),
            ("construct-6", "EVALUATION_PERIODIQUE", 120, new("Apte", [], null)),
            ("construct-7", "EVALUATION_PERIODIQUE", 60, new("InaptitudeTemporaire", [], 45)),
            ("construct-8", "EVALUATION_PERIODIQUE", 45, new("Apte", [], null)),
            ("zorg-6", "EVALUATION_PERIODIQUE", 150, new("Apte", [], null)),
            ("zorg-7", "EVALUATION_PERIODIQUE", 100, new("ApteAvecMesures", ["ETUDE_ERGONOMIQUE"], null)),
            ("zorg-8", "EVALUATION_PERIODIQUE", 75, new("Apte", [], null)),
        };
        foreach (var (cle, type, jours, decision) in specs)
        {
            await c.EssayerAsync($"Décision signée de {cle}", async () =>
            {
                if (c.Personnes.TryGetValue(cle, out var p) && p.DossierId is not null)
                {
                    var id = await DecisionSigneeAsync(c, p, type, jours, decision);
                    signees.Add(new JsonObject { ["personne"] = cle, ["decisionId"] = id.ToString() });
                }
            });
        }

        await c.EssayerAsync("Vaccinations", () => VaccinationsAsync(c, sante));
        await c.EssayerAsync("Examen en cours", () => ExamenEnCoursAsync(c, sante));
        await c.EssayerAsync("Décision en brouillon", () => BrouillonAsync(c, sante));
    }

    private static async Task<Guid> AssurerDossierAsync(Contexte c, PersonneDemo p, string cpmtSub)
    {
        var existant = await c.Api.GetAsync(Service.SurveillanceMedicale, $"dossiers?personneId={p.Id}", UtilisateurMedical, motif: true);
        if (existant.Ok)
        {
            return existant.Corps.Guid("id");
        }

        var cree = await c.Api.PostAsync(Service.SurveillanceMedicale, "dossiers", UtilisateurMedical, new { personneId = p.Id, gestionnaireCpmtId = cpmtSub }, motif: true);
        return EtapeAffilies.IdDe(Api.Exiger(cree, $"Ouverture du dossier de {p.Def.NomComplet}"));
    }

    private static async Task<Guid> DecisionSigneeAsync(Contexte c, PersonneDemo p, string type, int joursAvant, DecisionSpec spec)
    {
        var deja = Api.Exiger(await c.Api.GetAsync(Service.SurveillanceMedicale, $"decisions?personneId={p.Id}", UtilisateurMedical, motif: true), "Décisions de la personne").Liste();
        if (deja.Count > 0)
        {
            return deja[0].Guid("id");
        }

        var date = c.Aujourdhui.AddDays(-Math.Min(joursAvant, Math.Max(10, p.Def.JoursDepuisDebut - 14)));
        var examenId = await ExamenClotureAsync(c, p, type, date);
        var decisionId = await RedigerAsync(c, examenId, spec, date);
        var signature = await c.Api.PostAsync(Service.SurveillanceMedicale, $"decisions/{decisionId}/signature", UtilisateurMedical, motif: true);
        Api.Exiger(signature, $"Signature de la décision de {p.Def.NomComplet}");
        c.Info($"décision signée : {p.Def.NomComplet} ({p.Def.Langue}), {spec.Categorie}");
        return decisionId;
    }

    /// <summary>Examen ouvert, observation et biométrie fictives, puis clôture : ce que le CPMT ferait lors d'une consultation.</summary>
    private static async Task<Guid> ExamenClotureAsync(Contexte c, PersonneDemo p, string type, DateOnly date)
    {
        var examenId = await OuvrirExamenAsync(c, p, type, date);
        var cloture = await c.Api.PostAsync(Service.SurveillanceMedicale, $"examens/{examenId}/cloture", UtilisateurMedical, new { date }, motif: true);
        Api.Exiger(cloture, "Clôture de l'examen");
        return examenId;
    }

    private static async Task<Guid> OuvrirExamenAsync(Contexte c, PersonneDemo p, string type, DateOnly date)
    {
        var ouverture = await c.Api.PostAsync(
            Service.SurveillanceMedicale, "examens", UtilisateurMedical,
            new { dossierId = p.DossierId, typeExamen = type, affilieId = p.AffilieId, rendezVousId = (Guid?)null, date }, motif: true);
        var examenId = EtapeAffilies.IdDe(Api.Exiger(ouverture, $"Ouverture de l'examen de {p.Def.NomComplet}"));
        Api.Exiger(
            await c.Api.PutAsync(
                Service.SurveillanceMedicale, $"examens/{examenId}/observation", UtilisateurMedical,
                new { anamnese = "Donnée fictive de démonstration : aucun antécédent notable.", examenClinique = "Donnée fictive de démonstration : examen clinique sans particularité." },
                motif: true),
            "Observation clinique");
        var mesures = new object[]
        {
            new { code = "BIOM.IMC", valeur = 24.5m, unite = (string?)null, texte = (string?)null },
            new { code = "BIOM.TA_SYS", valeur = 122m, unite = (string?)null, texte = (string?)null },
            new { code = "BIOM.TA_DIA", valeur = 78m, unite = (string?)null, texte = (string?)null },
        };
        Api.Exiger(
            await c.Api.PostAsync(Service.SurveillanceMedicale, $"examens/{examenId}/actes", UtilisateurMedical,
                new { typeActe = "Biometrie", mesures, commentaire = "Valeurs fictives.", date }, motif: true),
            "Acte de biométrie");
        return examenId;
    }

    private static async Task<Guid> RedigerAsync(Contexte c, Guid examenId, DecisionSpec spec, DateOnly dateExamen)
    {
        var contenu = new
        {
            categorie = spec.Categorie,
            mesures = spec.Mesures,
            valideJusquAu = spec.ValiditeJours is { } j ? c.Aujourdhui.AddDays(j) : (DateOnly?)null,
            justification = "Texte fictif de démonstration.",
            recommandations = "Texte fictif de démonstration.",
        };
        _ = dateExamen;
        var cree = await c.Api.PostAsync(Service.SurveillanceMedicale, $"examens/{examenId}/decision", UtilisateurMedical, contenu, motif: true);
        return EtapeAffilies.IdDe(Api.Exiger(cree, "Rédaction de la décision"));
    }

    private static async Task VaccinationsAsync(Contexte c, JsonObject sante)
    {
        if (!c.Lieux.TryGetValue("bruxelles", out var centreId))
        {
            c.Avertir("Vaccinations : lieu Bruxelles absent, lot de vaccins non créé.");
            return;
        }

        var stock = Api.Exiger(await c.Api.GetAsync(Service.SurveillanceMedicale, $"centres/{centreId}/stock-vaccins", UtilisateurMedical, motif: true), "Stock de vaccins").Liste();
        var lotId = stock.SelectMany(s => s["lots"] as JsonArray ?? []).FirstOrDefault(l => l.Texte("numeroLot") == "DEMO-HB-001")?.Guid("id");
        if (lotId is null)
        {
            var lot = await c.Api.PostAsync(
                Service.SurveillanceMedicale, "lots-vaccins", UtilisateurMedical,
                new { centreId, vaccinCode = "HEPATITE_B", numeroLot = "DEMO-HB-001", peremption = c.Aujourdhui.AddDays(400), quantite = 50 }, motif: true);
            lotId = EtapeAffilies.IdDe(Api.Exiger(lot, "Réception du lot de vaccins"));
        }

        var vaccines = new JsonArray();
        sante["vaccinationsHepatiteB"] = vaccines;
        foreach (var cle in new[] { "zorg-6", "zorg-7", "zorg-9", "zorg-10" })
        {
            if (!c.Personnes.TryGetValue(cle, out var p) || p.DossierId is null)
            {
                continue;
            }

            await c.EssayerAsync($"Vaccination de {cle}", async () =>
            {
                var dossier = Api.Exiger(await c.Api.GetAsync(Service.SurveillanceMedicale, $"dossiers/{p.DossierId}", UtilisateurMedical, motif: true), "Lecture du dossier");
                var doses = (dossier.Corps?["vaccinations"] as JsonArray ?? [])
                    .Where(v => v.Texte("vaccinCode") == "HEPATITE_B").Select(v => int.Parse(v?["dose"]?.ToString() ?? "0", CultureInfo.InvariantCulture)).ToHashSet();
                foreach (var (dose, jours) in new[] { (1, 240), (2, 210) })
                {
                    if (doses.Contains(dose))
                    {
                        continue;
                    }

                    var administre = await c.Api.PostAsync(
                        Service.SurveillanceMedicale, $"dossiers/{p.DossierId}/vaccinations", UtilisateurMedical,
                        new { vaccinCode = "HEPATITE_B", dose, date = c.Aujourdhui.AddDays(-jours), lotId, remarque = "Dose fictive de démonstration." }, motif: true);
                    Api.Exiger(administre, $"Dose {dose} d'hépatite B");
                }

                vaccines.Add(cle);
            });
        }

        c.Info($"hépatite B doses 1 et 2 pour {vaccines.Count} travailleurs (dose 3 due depuis ~60 jours)");
    }

    private static async Task ExamenEnCoursAsync(Contexte c, JsonObject sante)
    {
        if (!c.Personnes.TryGetValue("boulangerie-11", out var p) || p.DossierId is null)
        {
            return;
        }

        var precedent = c.Precedent?["sante"]?["examenEnCours"];
        if (precedent is not null && (await c.Api.GetAsync(Service.SurveillanceMedicale, $"examens/{precedent.Texte("examenId")}", UtilisateurMedical, motif: true)).Ok)
        {
            sante["examenEnCours"] = precedent.DeepClone();
            c.Info("examen en cours déjà présent");
            return;
        }

        var examenId = await OuvrirExamenAsync(c, p, "EVALUATION_PERIODIQUE", c.Aujourdhui);
        sante["examenEnCours"] = new JsonObject { ["personne"] = p.Def.Cle, ["examenId"] = examenId.ToString() };
        c.Info($"examen en cours : {p.Def.NomComplet} (à clôturer en direct)");
    }

    private static async Task BrouillonAsync(Contexte c, JsonObject sante)
    {
        if (!c.Personnes.TryGetValue("boulangerie-10", out var p) || p.DossierId is null)
        {
            return;
        }

        var precedent = c.Precedent?["sante"]?["decisionBrouillon"];
        if (precedent is not null && (await c.Api.GetAsync(Service.SurveillanceMedicale, $"decisions/{precedent.Texte("decisionId")}/complete", UtilisateurMedical, motif: true)).Ok)
        {
            sante["decisionBrouillon"] = precedent.DeepClone();
            c.Info("décision en brouillon déjà présente");
            return;
        }

        var date = c.Aujourdhui.AddDays(-1);
        var examenId = await ExamenClotureAsync(c, p, "EVALUATION_PERIODIQUE", date);
        var decisionId = await RedigerAsync(c, examenId, new DecisionSpec("ApteAvecMesures", ["AMENAGEMENT_POSTE"], null), date);
        sante["decisionBrouillon"] = new JsonObject { ["personne"] = p.Def.Cle, ["examenId"] = examenId.ToString(), ["decisionId"] = decisionId.ToString() };
        c.Info($"décision en brouillon : {p.Def.NomComplet} (à signer en direct)");
    }
}

/// <summary>Étape 7 : réintégration (deux incapacités, un trajet avec invitation et évaluation en attente, un délai dépassé).</summary>
internal static class EtapeReintegration
{
    public static async Task ExecuterAsync(Contexte c)
    {
        c.Titre("7. Réintégration");
        var reintegration = new JsonObject();
        c.Manifeste["reintegration"] = reintegration;

        // Incapacité 1 : trajet demandé par le CPMT, travailleur invité, évaluation en attente.
        await c.EssayerAsync("Incapacité avec invitation", async () =>
        {
            var (incapaciteId, trajetId) = await IncapaciteAsync(c, "construct-9", jourDebut: 70, jourTrajet: 60, "Cpmt", jourInvitation: 55);
            reintegration["trajetInvite"] = new JsonObject { ["personne"] = "construct-9", ["incapaciteId"] = incapaciteId.ToString(), ["trajetId"] = trajetId.ToString() };
        });

        // Incapacité 2 : trajet demandé il y a 120 jours, jamais suivi d'invitation : délai dépassé.
        await c.EssayerAsync("Incapacité en dépassement de délai", async () =>
        {
            var (incapaciteId, trajetId) = await IncapaciteAsync(c, "boulangerie-12", jourDebut: 150, jourTrajet: 120, "Travailleur", jourInvitation: null);
            reintegration["trajetEnRetard"] = new JsonObject { ["personne"] = "boulangerie-12", ["incapaciteId"] = incapaciteId.ToString(), ["trajetId"] = trajetId.ToString() };
        });
    }

    private static async Task<(Guid IncapaciteId, Guid TrajetId)> IncapaciteAsync(
        Contexte c, string cle, int jourDebut, int jourTrajet, string initiateur, int? jourInvitation)
    {
        var p = c.Personnes.TryGetValue(cle, out var trouve) ? trouve : throw new EchecDemoException($"Travailleur {cle} absent.");
        var existantes = Api.Exiger(await c.Api.GetAsync(Service.Reintegration, $"incapacites?personneId={p.Id}", "cpmt", motif: true), "Incapacités").Liste();
        Guid incapaciteId;
        if (existantes.Count > 0)
        {
            incapaciteId = existantes[0].Guid("id");
        }
        else
        {
            var cree = await c.Api.PostAsync(
                Service.Reintegration, "incapacites", "cpmt",
                new { personneId = p.Id, affilieId = p.AffilieId, dateDebut = c.Aujourdhui.AddDays(-jourDebut), dateFinPrevue = (DateOnly?)null }, motif: true);
            incapaciteId = EtapeAffilies.IdDe(Api.Exiger(cree, $"Incapacité de {p.Def.NomComplet}"));
        }

        var detail = Api.Exiger(await c.Api.GetAsync(Service.Reintegration, $"incapacites/{incapaciteId}", "cpmt", motif: true), "Lecture de l'incapacité");
        var trajets = detail.Corps?["trajets"] as JsonArray ?? [];
        if (trajets.Count > 0)
        {
            return (incapaciteId, trajets[0].Guid("id"));
        }

        var trajet = await c.Api.PostAsync(
            Service.Reintegration, "trajets", "cpmt",
            new { incapaciteId, initiateur, dateDemande = c.Aujourdhui.AddDays(-jourTrajet) }, motif: true);
        var trajetId = EtapeAffilies.IdDe(Api.Exiger(trajet, $"Trajet de {p.Def.NomComplet}"));
        if (jourInvitation is { } jours)
        {
            Api.Exiger(
                await c.Api.PostAsync(Service.Reintegration, $"trajets/{trajetId}/invitation", "cpmt",
                    new { date = c.Aujourdhui.AddDays(-jours), recommandee = true, documentId = (Guid?)null }, motif: true),
                "Invitation du travailleur");
        }

        c.Info($"{p.Def.NomComplet} : incapacité depuis {jourDebut} jours, trajet demandé il y a {jourTrajet} jours{(jourInvitation is null ? " (aucune invitation : délai dépassé)" : ", travailleur invité (évaluation en attente)")}");
        return (incapaciteId, trajetId);
    }
}

/// <summary>Étape 8 : correspondance BCE de la Boulangerie dans Intégrations ; DIMONA n'est pas lancé (démonstration en direct).</summary>
internal static class EtapeIntegrations
{
    public static async Task ExecuterAsync(Contexte c)
    {
        c.Titre("8. Intégrations");
        if (!c.Affilies.TryGetValue("boulangerie", out var affilieId))
        {
            c.Avertir("Correspondance BCE ignorée : Boulangerie absente.");
            return;
        }

        var def = Catalogue.Affilies.First(a => a.Cle == "boulangerie");
        var reponse = await c.Api.PutAsync(
            Service.Integrations, "correspondances", "gestionnaire",
            new { typeExterne = "NumeroBce", valeurExterne = def.Bce, typeInterne = "Affilie", identifiantInterne = affilieId });
        Api.Exiger(reponse, "Correspondance BCE");
        c.Info($"correspondance BCE {def.Bce} <-> Boulangerie (DIMONA non lancé : à déclencher en direct)");
    }
}

/// <summary>Étape 9 : attributs Keycloak des comptes externes, puis actions avec leurs propres jetons (reprise, copie, proposition de risque).</summary>
internal static class EtapeComptesExternes
{
    private static readonly string[] AffiliesEmployeur = ["boulangerie", "construct"];

    public static async Task ExecuterAsync(Contexte c)
    {
        c.Titre("9. Comptes externes (Keycloak) et actions des portails");
        var externes = new JsonObject();
        c.Manifeste["externes"] = externes;

        await c.EssayerAsync("Attribut affilie_id de employeur", async () =>
        {
            var ids = AffiliesEmployeur.Where(c.Affilies.ContainsKey).Select(k => c.Affilies[k]).ToList();
            await c.Keycloak.DefinirAttributAsync("employeur", "affilie_id", ids);
            c.Jetons.Invalider("employeur");
            c.Info($"employeur : affilie_id = Boulangerie Démo + Construct Démo ({ids.Count} affiliés)");
        });

        if (c.Personnes.TryGetValue("boulangerie-0", out var etoile))
        {
            await c.EssayerAsync("Attribut personne_id de travailleur", async () =>
            {
                await c.Keycloak.DefinirAttributAsync("travailleur", "personne_id", [etoile.Id]);
                c.Jetons.Invalider("travailleur");
                c.Info($"travailleur : personne_id = {etoile.Def.NomComplet}");
            });
            externes["travailleuse"] = new JsonObject { ["nom"] = etoile.Def.NomComplet, ["personneId"] = etoile.Id.ToString(), ["niss"] = etoile.Def.Niss };
        }

        await c.EssayerAsync("Annonce de reprise (employeur)", async () => await AnnoncerRepriseAsync(c, externes));
        await c.EssayerAsync("Demande de copie (travailleur)", async () => await DemanderCopieAsync(c, externes));
        await c.EssayerAsync("Proposition de risque (employeur)", async () => await ProposerRisqueAsync(c, externes));
    }

    private static async Task AnnoncerRepriseAsync(Contexte c, JsonObject externes)
    {
        if (!c.Personnes.TryGetValue("construct-10", out var p))
        {
            return;
        }

        var reponse = await c.Api.PostAsync(
            Service.Obligations, "reprises", "employeur",
            new { personneId = p.Id, affilieId = p.AffilieId, dateReprise = c.Aujourdhui.AddDays(7), debutAbsence = c.Aujourdhui.AddDays(-45) });
        var cree = Api.Exiger(reponse, "Annonce de reprise");
        externes["reprise"] = new JsonObject { ["personne"] = p.Def.Cle, ["repriseId"] = cree.Corps?["repriseId"]?.ToString() };
        c.Info($"reprise annoncée par l'employeur : {p.Def.NomComplet}");
    }

    private static async Task DemanderCopieAsync(Contexte c, JsonObject externes)
    {
        var miennes = await c.Api.GetAsync(Service.SurveillanceMedicale, "demandes-copie/miennes", "travailleur");
        if (miennes.Ok && miennes.Liste().Count > 0)
        {
            return;
        }

        Api.Exiger(miennes.Ok ? await c.Api.PostAsync(Service.SurveillanceMedicale, "demandes-copie", "travailleur") : miennes, "Demande de copie du dossier");
        externes["demandeCopie"] = true;
        c.Info("demande de copie du dossier déposée par la travailleuse (à traiter par le CPMT)");
    }

    private static async Task ProposerRisqueAsync(Contexte c, JsonObject externes)
    {
        if (!c.Postes.TryGetValue("boulangerie|Vendeur en boutique", out var posteId))
        {
            return;
        }

        const string motif = "Démo : nouvelle exposition à des charges lourdes (portail employeur)";
        var existantes = Api.Exiger(await c.Api.GetAsync(Service.PostesRisques, $"propositions-poste-risque?posteId={posteId}", "cpmt"), "Propositions").Liste();
        if (existantes.Any(p => p.Texte("motif") == motif))
        {
            return;
        }

        var reponse = await c.Api.PostAsync(
            Service.PostesRisques, $"postes/{posteId}/propositions", "employeur",
            new
            {
                motif,
                valideDu = c.Aujourdhui,
                lignes = new[] { new { type = "Ajout", risqueCode = "EX.ERGO.CHARGES", niveauExposition = "Moyen" } },
                avisCppt = (object?)null,
            });
        var id = EtapeAffilies.IdDe(Api.Exiger(reponse, "Proposition de l'employeur"));
        externes["propositionRisque"] = id.ToString();
        c.Info("proposition poste-risque soumise par l'employeur (à valider par le CPMT)");
    }
}
