using System.Globalization;
using System.Text.Json.Nodes;

namespace Sepp.Outils.DonneesDemo;

/// <summary>Comptes de démonstration du realm local (mot de passe commun <see cref="Jetons.MotDePasse"/>).</summary>
internal static class Comptes
{
    public static readonly IReadOnlyList<(string Utilisateur, string Role)> Liste =
    [
        ("cpmt", "CPMT : tableau de bord, consultation, décisions, validation des propositions"),
        ("cpmt.dirigeant", "CPMT dirigeant : audit médical, modèles médicaux"),
        ("infirmier", "Infirmier : examens, vaccinations"),
        ("assistant", "Assistant médical : accueil et salle d'attente"),
        ("planificateur", "Planificateur : agenda, rendez-vous, convocations"),
        ("responsable", "Responsable de centre : lieux, ressources, congés"),
        ("gestionnaire", "Gestionnaire de dossiers : affiliés, travailleurs, intégrations"),
        ("admin", "Administrateur fonctionnel : paramètres légaux, risques, modèles"),
        ("dpo", "DPO : audit"),
        ("employeur", "Portail employeur (affiliés Boulangerie Démo et Construct Démo)"),
        ("travailleur", "Portail travailleur (la travailleuse à chercher)"),
    ];

    /// <summary>
    /// Les comptes <c>assistant</c> et <c>responsable</c> sont fournis par le realm local (lot 0A, #390). Sur un volume Keycloak plus ancien
    /// qui ne les a pas, ils sont créés par l'API d'administration (poste de développement uniquement) ; le responsable est requis pour
    /// les lieux et ressources.
    /// </summary>
    public static async Task AssurerAsync(Contexte c)
    {
        foreach (var (utilisateur, role, prenom, nom) in new[]
                 {
                     ("responsable", "responsable-centre", "Rose", "Responsable"),
                     ("assistant", "assistant-medical", "Alice", "Assistante"),
                 })
        {
            await c.EssayerAsync($"Compte {utilisateur}", async () =>
            {
                if (await c.Keycloak.AssurerUtilisateurAsync(utilisateur, role, prenom, nom))
                {
                    c.Avertir($"Compte « {utilisateur} » absent du realm local (volume Keycloak antérieur au lot 0A) : créé par l'API d'administration de Keycloak, rôle {role}, mot de passe {Jetons.MotDePasse}.");
                }
            });
        }
    }
}

/// <summary>Étape 5 : lieux, ressources, modèles d'agenda, créneaux de J à J+30 et rendez-vous convoqués.</summary>
internal static class EtapePlanification
{
    private static readonly string[] TypesActe =
    [
        "EVALUATION_PERIODIQUE", "EVALUATION_PREALABLE", "ACTES_MEDICAUX_SUPPLEMENTAIRES", "EXAMEN_REPRISE", "CONSULTATION_SPONTANEE",
    ];

    private static readonly string[] TypesConseiller = ["EVALUATION_PERIODIQUE", "EVALUATION_PREALABLE", "ACTES_MEDICAUX_SUPPLEMENTAIRES", "EXAMEN_REPRISE"];

    private static readonly string[] JoursOuvres = ["lundi", "mardi", "mercredi", "jeudi", "vendredi"];

    public static async Task ExecuterAsync(Contexte c)
    {
        c.Titre("5. Planification");
        await c.EssayerAsync("Lieux", () => AssurerLieuxAsync(c));
        await c.EssayerAsync("Ressources", () => AssurerRessourcesAsync(c));
        await c.EssayerAsync("Durées standard", async () =>
        {
            foreach (var (acte, minutes) in new[] { ("EVALUATION_PERIODIQUE", 30), ("EVALUATION_PREALABLE", 30), ("ACTES_MEDICAUX_SUPPLEMENTAIRES", 30), ("EXAMEN_REPRISE", 30), ("CONSULTATION_SPONTANEE", 20) })
            {
                Api.Exiger(
                    await c.Api.PutAsync(Service.Planification, "durees-standard", "responsable", new { typeActe = acte, dureeMinutes = minutes, ressourceId = (Guid?)null }),
                    $"Durée standard {acte}");
            }
        });
        await c.EssayerAsync("Modèles d'agenda et créneaux", () => AssurerAgendasAsync(c));
        await c.EssayerAsync("Rendez-vous", () => PlanifierAsync(c));
    }

    private static async Task AssurerLieuxAsync(Contexte c)
    {
        var existants = Api.Exiger(await c.Api.GetAsync(Service.Planification, "lieux", "responsable"), "Liste des lieux").Liste();
        foreach (var (cle, nom, adresse, codePostal, lat, lon) in new[]
                 {
                     ("bruxelles", "Centre SEPP Bruxelles", "Boulevard Anspach 1", "1000", 50.8503, 4.3517),
                     ("gand", "Centre SEPP Gand", "Veldstraat 10", "9000", 51.0543, 3.7174),
                 })
        {
            var trouve = existants.FirstOrDefault(l => l.Texte("nom") == nom);
            if (trouve is not null)
            {
                c.Lieux[cle] = trouve.Guid("id");
                continue;
            }

            var cree = await c.Api.PostAsync(
                Service.Planification, "lieux", "responsable",
                new { type = "CentreFixe", nom, adresse, codePostal, latitude = lat, longitude = lon, affilieId = (Guid?)null, siteId = (Guid?)null });
            c.Lieux[cle] = EtapeAffilies.IdDe(Api.Exiger(cree, $"Création du lieu {nom}"));
        }

        c.Info($"{c.Lieux.Count} lieux");
    }

    private static async Task AssurerRessourcesAsync(Contexte c)
    {
        var existantes = Api.Exiger(await c.Api.GetAsync(Service.Planification, "ressources", "responsable"), "Liste des ressources").Liste();
        foreach (var (cle, utilisateur, type, lieu, titre) in new[]
                 {
                     ("cpmt", "cpmt", "Conseiller", "bruxelles", "Dr"),
                     ("infirmier", "infirmier", "Infirmier", "bruxelles", "Inf."),
                     ("cpmt-gand", "cpmt.dirigeant", "Conseiller", "gand", "Dr"),
                 })
        {
            if (!c.Lieux.TryGetValue(lieu, out var lieuId))
            {
                continue;
            }

            // La référence de la ressource est le sub du jeton de l'utilisateur : Planification limite les modèles d'agenda au CPMT titulaire.
            var sub = await c.Jetons.SubAsync(utilisateur);
            var trouve = existantes.FirstOrDefault(r => r.Texte("referenceId") == sub);
            if (trouve is not null)
            {
                c.Ressources[cle] = trouve.Guid("id");
                continue;
            }

            var profil = await c.Keycloak.TrouverUtilisateurAsync(utilisateur);
            var libelle = $"{titre} {profil?["firstName"]} {profil?["lastName"]} - {(lieu == "gand" ? "Gand" : "Bruxelles")}".Replace("  ", " ", StringComparison.Ordinal);
            var cree = await c.Api.PostAsync(
                Service.Planification, "ressources", "responsable",
                new { type, libelle, referenceId = sub, competences = type == "Conseiller" ? TypesActe : TypesActe[..2], lieuId });
            c.Ressources[cle] = EtapeAffilies.IdDe(Api.Exiger(cree, $"Création de la ressource {libelle}"));
        }

        c.Info($"{c.Ressources.Count} ressources (référence = sub du jeton)");
    }

    private static async Task AssurerAgendasAsync(Contexte c)
    {
        foreach (var (cle, lieu, utilisateur) in new[] { ("cpmt", "bruxelles", "responsable"), ("infirmier", "bruxelles", "responsable"), ("cpmt-gand", "gand", "responsable") })
        {
            if (!c.Ressources.TryGetValue(cle, out var ressourceId) || !c.Lieux.TryGetValue(lieu, out var lieuId))
            {
                continue;
            }

            var modeles = Api.Exiger(await c.Api.GetAsync(Service.Planification, $"modeles-agenda?ressourceId={ressourceId}", utilisateur), "Modèles d'agenda").Liste();
            Guid modeleId;
            if (modeles.Count > 0)
            {
                modeleId = modeles[0].Guid("id");
            }
            else
            {
                var plages = PlagesDe(cle);
                var cree = await c.Api.PostAsync(
                    Service.Planification, "modeles-agenda", utilisateur,
                    new { ressourceId, lieuId, valideDu = c.Aujourdhui, valideJusquAu = (DateOnly?)null, plages });
                modeleId = EtapeAffilies.IdDe(Api.Exiger(cree, $"Modèle d'agenda de {cle}"));
            }

            var generation = await c.Api.PostAsync(
                Service.Planification, $"modeles-agenda/{modeleId}/creneaux", "responsable", new { du = c.Aujourdhui, au = c.Aujourdhui.AddDays(30) });
            Api.Exiger(generation, $"Génération des créneaux de {cle}");
            c.Info($"{cle} : {generation.Corps?["crees"]} créneaux créés, {generation.Corps?["dejaExistants"]} déjà présents");
        }
    }

    private static List<object> PlagesDe(string ressource)
    {
        var plages = new List<object>();
        foreach (var jour in JoursOuvres)
        {
            plages.Add(Plage(jour, "07:30", "11:30", "EVALUATION_PERIODIQUE", 30, false));
            if (ressource == "infirmier")
            {
                plages.Add(Plage(jour, "13:00", "16:00", "EVALUATION_PREALABLE", 30, false));
                continue;
            }

            plages.Add(Plage(jour, "11:30", "12:00", "CONSULTATION_SPONTANEE", 20, true));
            plages.Add(Plage(jour, "13:00", "15:00", "EVALUATION_PREALABLE", 30, false));
            plages.Add(Plage(jour, "15:00", "16:30", "ACTES_MEDICAUX_SUPPLEMENTAIRES", 30, false));
            plages.Add(Plage(jour, "16:30", "17:30", "EXAMEN_REPRISE", 30, false));
        }

        return plages;
    }

    private static object Plage(string jour, string debut, string fin, string typeActe, int minutes, bool urgence) => new
    {
        jour,
        debut,
        fin,
        typeActe,
        dureeMinutes = minutes,
        reserveUrgence = urgence,
        ouvertEnLigne = !urgence,
        ressourcesAssociees = (IReadOnlyList<Guid>?)null,
    };

    private static async Task PlanifierAsync(Contexte c)
    {
        if (c.Personnes.Count == 0)
        {
            c.Avertir("Aucun travailleur : pas de rendez-vous.");
            return;
        }

        // Cinq rendez-vous aujourd'hui pour le CPMT, puis des rendez-vous les jours suivants.
        var aujourdhui = new[] { "boulangerie-0", "boulangerie-1", "boulangerie-2", "construct-0", "construct-1" };
        var plusTard = new (string Ressource, string Personne)[]
        {
            ("cpmt", "boulangerie-3"), ("cpmt", "boulangerie-4"), ("cpmt", "construct-2"), ("cpmt", "boulangerie-5"), ("cpmt", "construct-3"),
            ("cpmt", "boulangerie-6"), ("cpmt", "construct-4"), ("infirmier", "boulangerie-7"), ("infirmier", "construct-5"), ("infirmier", "boulangerie-8"),
            ("cpmt-gand", "zorg-0"), ("cpmt-gand", "zorg-1"), ("cpmt-gand", "zorg-2"), ("cpmt-gand", "zorg-3"), ("cpmt-gand", "zorg-4"), ("cpmt-gand", "zorg-5"),
        };

        await AttendreObligationsAsync(c, aujourdhui.Concat(plusTard.Select(p => p.Personne)));

        var reservesAujourdhui = 0;
        foreach (var cle in aujourdhui)
        {
            await c.EssayerAsync($"Rendez-vous du jour de {cle}", async () =>
            {
                if (await PlanifierUnAsync(c, cle, "cpmt", aujourdhui: true, decalageJours: 0))
                {
                    reservesAujourdhui++;
                }
            });
        }

        if (reservesAujourdhui < aujourdhui.Length)
        {
            c.Avertir($"Seulement {reservesAujourdhui} rendez-vous aujourd'hui sur {aujourdhui.Length} : plus assez de créneaux libres dans la journée (week-end, jour férié ou fin de journée). Les autres sont reportés aux jours ouvrables suivants.");
        }

        var decalage = 1;
        foreach (var (ressource, personne) in plusTard)
        {
            await c.EssayerAsync($"Rendez-vous de {personne}", async () =>
            {
                await PlanifierUnAsync(c, personne, ressource, aujourdhui: false, decalageJours: decalage);
            });
            decalage = (decalage % 12) + 1;
        }
    }

    private static async Task AttendreObligationsAsync(Contexte c, IEnumerable<string> cles)
    {
        var attendues = cles.Where(k => c.Personnes.ContainsKey(k)).ToList();
        var recalcule = false;
        var dernier = -1;
        var stable = 0;
        await c.AttendreAsync(
            $"obligations de {attendues.Count} travailleurs (bus de développement : affectations, profils de risques, projections)",
            async () =>
            {
                // Certains travailleurs n'ont aucune obligation (poste sans risque) : on attend que le nombre de travailleurs
                // ayant des obligations cesse de croître, plutôt que de les exiger tous.
                var avecObligations = 0;
                foreach (var cle in attendues)
                {
                    var liste = await c.Api.GetAsync(Service.Obligations, $"personnes/{c.Personnes[cle].Id}/obligations", "cpmt");
                    if (liste.Ok && liste.Liste().Count > 0)
                    {
                        avecObligations++;
                    }
                }

                if (avecObligations == 0 && !recalcule)
                {
                    // Une seule relance du calcul (SAN-04) si la première interrogation ne voit rien.
                    recalcule = true;
                    foreach (var affilieId in c.Affilies.Values)
                    {
                        await c.Api.PostAsync(Service.Obligations, "recalcul", "cpmt", new { personneId = (Guid?)null, affilieId });
                    }
                }

                stable = avecObligations == dernier ? stable + 1 : 0;
                dernier = avecObligations;
                return avecObligations > 0 && (avecObligations == attendues.Count || stable >= 3);
            },
            delaiSecondes: c.Options.DelaiAttenteSecondes * 2);
        c.Info($"{Math.Max(dernier, 0)} travailleurs sur {attendues.Count} ont des obligations");
    }

    private static async Task<bool> PlanifierUnAsync(Contexte c, string clePersonne, string cleRessource, bool aujourdhui, int decalageJours)
    {
        if (!c.Personnes.TryGetValue(clePersonne, out var personne) || !c.Ressources.TryGetValue(cleRessource, out var ressourceId))
        {
            return false;
        }

        var existants = Api.Exiger(await c.Api.GetAsync(Service.Planification, $"rendez-vous?personneId={personne.Id}", "planificateur"), "Rendez-vous de la personne").Liste();
        if (existants.Any(r => r.Texte("statut") is "Planifie" or "Arrive" or "EnSalle"))
        {
            return true;
        }

        var obligations = (await c.Api.GetAsync(Service.Obligations, $"personnes/{personne.Id}/obligations?ouvertesSeulement=true", "cpmt")).Liste()
            .Where(o => o["rendezVousId"] is null).ToList();
        var obligation = obligations.FirstOrDefault(o => TypesAdmis(cleRessource).Contains(o.Texte("type") ?? string.Empty));
        var typeActe = obligation?.Texte("type") ?? "EVALUATION_PERIODIQUE";

        var debut = c.Maintenant.AddMinutes(10);
        var fin = c.DebutDeJour(c.Aujourdhui.AddDays(1));
        if (!aujourdhui)
        {
            debut = c.DebutDeJour(c.Aujourdhui.AddDays(decalageJours));
            fin = c.DebutDeJour(c.Aujourdhui.AddDays(decalageJours + 14));
        }

        var creneau = await TrouverCreneauAsync(c, ressourceId, typeActe, debut, fin);
        if (creneau is null && aujourdhui)
        {
            creneau = await TrouverCreneauAsync(c, ressourceId, typeActe, fin, fin.AddDays(14));
        }

        if (creneau is null)
        {
            c.Avertir($"Aucun créneau « {typeActe} » libre pour {personne.Def.NomComplet}.");
            return false;
        }

        var corps = new
        {
            creneauId = creneau.Guid("id"),
            personneId = personne.Id,
            affilieId = personne.AffilieId,
            obligationIds = obligation is null ? Array.Empty<Guid>() : [obligation.Guid("id")],
            canal = (string?)null,
            recommande = (bool?)null,
            convoquer = true,
        };
        Api.Exiger(await c.Api.PostAsync(Service.Planification, "rendez-vous", "planificateur", corps), $"Rendez-vous de {personne.Def.NomComplet}");
        var heure = DateTimeOffset.Parse(creneau.Texte("debut")!, CultureInfo.InvariantCulture);
        c.Info($"RDV {personne.Def.NomComplet} ({cleRessource}) le {TimeZoneInfo.ConvertTime(heure, c.Fuseau):dd/MM HH:mm}, {typeActe}{(obligation is null ? " (sans obligation)" : string.Empty)}");
        return TimeZoneInfo.ConvertTime(heure, c.Fuseau).Date == c.Aujourdhui.ToDateTime(TimeOnly.MinValue);
    }

    /// <summary>Types d'acte pour lesquels la ressource a des créneaux (l'infirmier n'a que l'évaluation périodique et préalable).</summary>
    private static string[] TypesAdmis(string ressource) =>
        ressource == "infirmier" ? ["EVALUATION_PERIODIQUE", "EVALUATION_PREALABLE"] : TypesConseiller;

    private static async Task<JsonNode?> TrouverCreneauAsync(Contexte c, Guid ressourceId, string typeActe, DateTimeOffset du, DateTimeOffset au)
    {
        var reponse = await c.Api.GetAsync(
            Service.Planification, $"creneaux?du={Api.Q(du)}&au={Api.Q(au)}&ressourceId={ressourceId}&statut=Libre", "planificateur");
        Api.Exiger(reponse, "Créneaux libres");
        return reponse.Liste()
            .Where(k => k.Texte("typeActe") == typeActe && k["reserveUrgence"]?.GetValue<bool>() != true)
            .Where(k => DateTimeOffset.Parse(k.Texte("debut")!, CultureInfo.InvariantCulture) > du)
            .OrderBy(k => DateTimeOffset.Parse(k.Texte("debut")!, CultureInfo.InvariantCulture))
            .FirstOrDefault();
    }
}
