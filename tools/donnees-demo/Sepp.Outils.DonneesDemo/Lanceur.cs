using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sepp.Outils.DonneesDemo;

/// <summary>Résultat d'un contrôle de parcours : le parcours a-t-il du contenu à montrer ?</summary>
internal sealed record Controle(string Parcours, bool Ok, string Detail);

/// <summary>Point d'entrée : enchaîne les étapes 1 à 10 du plan de démonstration (#393).</summary>
internal static class Lanceur
{
    public static async Task<int> ExecuterAsync(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Options options;
        try
        {
            options = Options.Lire(args);
        }
        catch (EchecDemoException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 64;
        }

        var c = new Contexte(options);
        Console.WriteLine($"Données de démonstration Veilla : {c.Aujourdhui:yyyy-MM-dd} (Europe/Brussels), hôte {options.Hote}");
        Console.WriteLine("Données fictives uniquement ; flux mot de passe sur veilla-dev-cli et API d'administration de Keycloak : poste de développement local.");

        try
        {
            if (!await ServicesPretsAsync(c))
            {
                return 2;
            }

            await Comptes.AssurerAsync(c);
            foreach (var etape in new Func<Contexte, Task>[]
                     {
                         EtapeReferentiels.ExecuterAsync, EtapeModeles.ExecuterAsync, EtapeAffilies.ExecuterAsync, EtapePostes.ExecuterAsync, EtapePersonnes.ExecuterAsync,
                         EtapePlanification.ExecuterAsync, EtapeSurveillance.ExecuterAsync, EtapeReintegration.ExecuterAsync,
                         EtapeIntegrations.ExecuterAsync, EtapeComptesExternes.ExecuterAsync,
                     })
            {
                await c.EssayerAsync("Étape interrompue", () => etape(c));
            }

            c.Titre("10. Contrôles et résumé");
            var controles = await Controles.ExecuterAsync(c);
            await EcrireManifesteAsync(c, controles);
            Resume.Afficher(c, controles);
            return c.Avertissements.Count == 0 && controles.All(x => x.Ok) ? 0 : 1;
        }
        catch (EchecDemoException ex)
        {
            Console.Error.WriteLine($"\nÉchec de l'amorçage : {ex.Message}");
            return 2;
        }
    }

    private static async Task<bool> ServicesPretsAsync(Contexte c)
    {
        c.Titre("0. Services");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var tous = Enum.GetValues<Service>();
        foreach (var service in tous)
        {
            var prets = await c.AttendreAsync(
                $"{service} (:{(int)service}/health/ready)",
                async () =>
                {
                    try
                    {
                        using var reponse = await http.GetAsync(new Uri($"http://{c.Options.Hote}:{(int)service}/health/ready"));
                        return reponse.IsSuccessStatusCode;
                    }
                    catch (HttpRequestException)
                    {
                        return false;
                    }
                    catch (TaskCanceledException)
                    {
                        return false;
                    }
                },
                delaiSecondes: 30);
            if (!prets)
            {
                Console.Error.WriteLine("Les services ne sont pas démarrés : lancez deploy/local/demo.sh (ou demo.ps1), ou « docker compose -f deploy/local/compose.yaml up -d --build ».");
                return false;
            }
        }

        return true;
    }

    private static async Task EcrireManifesteAsync(Contexte c, IReadOnlyList<Controle> controles)
    {
        var manifeste = c.Manifeste;
        manifeste["genereLe"] = c.Maintenant.ToString("O", CultureInfo.InvariantCulture);
        manifeste["aujourdhui"] = c.Aujourdhui.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        manifeste["motDePasseDemo"] = Jetons.MotDePasse;
        manifeste["comptes"] = new JsonArray([.. Comptes.Liste.Select(x => (JsonNode)new JsonObject { ["utilisateur"] = x.Utilisateur, ["usage"] = x.Role })]);
        manifeste["affilies"] = new JsonArray([.. Catalogue.Affilies.Where(a => c.Affilies.ContainsKey(a.Cle)).Select(a => (JsonNode)new JsonObject
        {
            ["cle"] = a.Cle,
            ["denomination"] = a.Denomination,
            ["bce"] = a.Bce,
            ["id"] = c.Affilies[a.Cle].ToString(),
            ["langue"] = a.Langue,
        })]);
        manifeste["lieux"] = new JsonObject([.. c.Lieux.Select(l => new KeyValuePair<string, JsonNode?>(l.Key, l.Value.ToString()))]);
        manifeste["ressources"] = new JsonObject([.. c.Ressources.Select(l => new KeyValuePair<string, JsonNode?>(l.Key, l.Value.ToString()))]);
        manifeste["personnes"] = new JsonArray([.. c.Personnes.Values.Select(p => (JsonNode)new JsonObject
        {
            ["cle"] = p.Def.Cle,
            ["nom"] = p.Def.NomComplet,
            ["langue"] = p.Def.Langue,
            ["niss"] = p.Def.Niss,
            ["poste"] = p.Def.PosteIntitule,
            ["id"] = p.Id.ToString(),
            ["dossierId"] = p.DossierId?.ToString(),
        })]);
        manifeste["controles"] = new JsonArray([.. controles.Select(x => (JsonNode)new JsonObject { ["parcours"] = x.Parcours, ["ok"] = x.Ok, ["detail"] = x.Detail })]);
        manifeste["avertissements"] = new JsonArray([.. c.Avertissements.Select(a => (JsonNode)JsonValue.Create(a)!)]);

        Directory.CreateDirectory(Path.GetDirectoryName(c.Options.Manifeste)!);
        await File.WriteAllTextAsync(c.Options.Manifeste, manifeste.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        c.Info($"manifeste écrit : {c.Options.Manifeste}");
    }
}

/// <summary>Contrôles par les API : chaque parcours de démonstration a-t-il du contenu ?</summary>
internal static class Controles
{
    public static async Task<IReadOnlyList<Controle>> ExecuterAsync(Contexte c)
    {
        var resultats = new List<Controle>();
        async Task Verifier(string parcours, Func<Task<(bool Ok, string Detail)>> action)
        {
            try
            {
                var (ok, detail) = await action();
                resultats.Add(new Controle(parcours, ok, detail));
            }
            catch (EchecDemoException ex)
            {
                resultats.Add(new Controle(parcours, false, ex.Message));
            }

            var dernier = resultats[^1];
            Console.WriteLine($"   [{(dernier.Ok ? "OK" : "KO")}] {dernier.Parcours} : {dernier.Detail}");
        }

        await Verifier("Obligations des affiliés", async () =>
        {
            var details = new List<string>();
            var ok = true;
            foreach (var def in Catalogue.Affilies)
            {
                if (!c.Affilies.TryGetValue(def.Cle, out var id))
                {
                    ok = false;
                    continue;
                }

                var attendu = await c.AttendreAsync(
                    $"obligations de {def.Denomination}",
                    async () => (await c.Api.GetAsync(Service.Obligations, $"affilies/{id}/obligations", "cpmt")).Liste().Count > 0,
                    delaiSecondes: 90);
                var n = (await c.Api.GetAsync(Service.Obligations, $"affilies/{id}/obligations", "cpmt")).Liste().Count;
                ok &= attendu;
                details.Add($"{def.Cle} {n}");
            }

            return (ok, string.Join(", ", details));
        });

        await Verifier("P1 Rendez-vous du jour du CPMT", async () =>
        {
            var ressource = c.Ressources.GetValueOrDefault("cpmt");
            var du = c.DebutDeJour(c.Aujourdhui);
            var liste = Api.Exiger(
                await c.Api.GetAsync(Service.Planification, $"rendez-vous?ressourceId={ressource}&du={Api.Q(du)}&au={Api.Q(du.AddDays(14))}", "cpmt"),
                "Rendez-vous du CPMT").Liste();
            var actifs = liste.Where(r => r.Texte("statut") is not ("Annule" or "Absent"))
                .Select(r => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.Parse(r.Texte("debut")!, CultureInfo.InvariantCulture), c.Fuseau).DateTime))
                .ToList();
            var aujourdhui = actifs.Count(d => d == c.Aujourdhui);
            if (aujourdhui > 0)
            {
                return (true, $"{aujourdhui} rendez-vous aujourd'hui");
            }

            // Week-end, jour férié ou fin de journée : les rendez-vous sont au prochain jour ouvrable.
            var premier = actifs.Count > 0 ? actifs.Min() : (DateOnly?)null;
            return premier is { } jour
                ? (true, $"aucun créneau libre aujourd'hui ; {actifs.Count(d => d == jour)} rendez-vous le prochain jour ouvrable ({jour:dd/MM/yyyy})")
                : (false, "aucun rendez-vous dans les 14 prochains jours");
        });

        await Verifier("P1 Propositions poste-risque à valider", async () =>
        {
            var n = Api.Exiger(await c.Api.GetAsync(Service.PostesRisques, "propositions-poste-risque?statut=Soumise", "cpmt"), "Propositions soumises").Liste().Count;
            return (n >= 1, $"{n} proposition(s) soumise(s)");
        });

        await Verifier("P4 Décision en brouillon", async () =>
        {
            var id = c.Manifeste["sante"]?["decisionBrouillon"]?["decisionId"]?.ToString();
            if (id is null)
            {
                return (false, "aucune décision en brouillon");
            }

            var r = Api.Exiger(await c.Api.GetAsync(Service.SurveillanceMedicale, $"decisions/{id}/complete", "cpmt", motif: true), "Décision en brouillon");
            return (r.Corps?["resume"]?["statut"]?.ToString() == "Brouillon", $"statut {r.Corps?["resume"]?["statut"]}");
        });

        foreach (var (langue, cle) in new[] { ("FR", "boulangerie-0"), ("NL", "zorg-6") })
        {
            await Verifier($"P4 Documents de décision ({langue})", async () =>
            {
                var id = (c.Manifeste["sante"]?["decisionsSignees"] as JsonArray)?.FirstOrDefault(d => d.Texte("personne") == cle)?.Texte("decisionId");
                if (id is null)
                {
                    return (false, $"pas de décision signée pour {cle}");
                }

                IReadOnlyList<JsonNode> documents = [];
                var ok = await c.AttendreAsync(
                    $"documents de la décision {langue}",
                    async () =>
                    {
                        var r = await c.Api.GetAsync(Service.Documents, $"documents?objetType=decision&objetId={id}", "cpmt", motif: true);
                        documents = r.Liste();
                        return documents.Count >= 2;
                    },
                    delaiSecondes: 120);
                var langues = string.Join("/", documents.Select(d => d.Texte("langue") ?? "?").Distinct());
                return (ok, $"{documents.Count} document(s), langue(s) {langues}");
            });
        }

        await Verifier("P7 Vaccinations (rappel dû)", async () =>
        {
            if (!c.Personnes.TryGetValue("zorg-9", out var p) || p.DossierId is null)
            {
                return (false, "dossier absent");
            }

            IReadOnlyList<JsonNode> rappels = [];
            var ok = await c.AttendreAsync(
                "rappel vaccinal en retard (projection des profils de risques)",
                async () =>
                {
                    var r = await c.Api.GetAsync(Service.SurveillanceMedicale, $"dossiers/{p.DossierId}/vaccinations/rappels", "cpmt", motif: true);
                    rappels = r.Liste();
                    return rappels.Any(x => x["enRetard"]?.GetValue<bool>() == true);
                },
                delaiSecondes: 90);
            return (ok, $"{rappels.Count} rappel(s), dont {rappels.Count(x => x["enRetard"]?.GetValue<bool>() == true)} en retard");
        });

        await Verifier("P8 Réintégration (trajets, délais)", async () =>
        {
            var affilie = c.Affilies.GetValueOrDefault("construct");
            var trajets = Api.Exiger(await c.Api.GetAsync(Service.Reintegration, $"affilies/{affilie}/trajets", "gestionnaire", motif: true), "Trajets").Liste();
            var tableau = Api.Exiger(await c.Api.GetAsync(Service.Reintegration, "tableau-de-bord/delais", "cpmt", motif: true), "Tableau de bord des délais");
            return (trajets.Count >= 1, $"{trajets.Count} trajet(s) chez Construct Démo ; tableau de bord des délais : {tableau.Texte.Length} octets");
        });

        await Verifier("P11 Documents visibles par le BFF travailleur", async () =>
        {
            IReadOnlyList<JsonNode> documents = [];
            var ok = await c.AttendreAsync(
                "documents du travailleur (BFF travailleur)",
                async () =>
                {
                    var r = await c.Api.GetAsync(Service.BffTravailleur, "documents", "travailleur");
                    documents = r.Liste();
                    return documents.Count >= 1;
                },
                delaiSecondes: 120);
            return (ok, $"{documents.Count} document(s) pour la travailleuse");
        });

        await Verifier("Employeur : périmètre Keycloak", async () =>
        {
            var id = c.Affilies.GetValueOrDefault("boulangerie");
            var r = await c.Api.GetAsync(Service.Obligations, $"affilies/{id}/obligations", "employeur");
            return (r.Ok, r.Ok ? $"{r.Liste().Count} obligation(s) visibles par l'employeur" : r.Resume());
        });

        await Verifier("Communications : envois", async () =>
        {
            var r = await c.Api.GetAsync(Service.Communications, "messages", "gestionnaire");
            return (r.Ok, r.Ok ? $"{r.Liste().Count} message(s)" : r.Resume());
        });

        return resultats;
    }
}

/// <summary>Résumé imprimé : comptes, parcours et travailleuse à chercher.</summary>
internal static class Resume
{
    public static void Afficher(Contexte c, IReadOnlyList<Controle> controles)
    {
        Console.WriteLine("\n==================== DÉMO PRÊTE ====================");
        Console.WriteLine($"Mot de passe commun de tous les comptes : {Jetons.MotDePasse}   (Keycloak : http://{c.Options.Hote}:8180, realm veilla)");
        Console.WriteLine("Application interne : http://localhost:8081   Portail employeur : http://localhost:8082   Portail travailleur : http://localhost:8083");
        Console.WriteLine("\nComptes :");
        foreach (var (utilisateur, usage) in Comptes.Liste)
        {
            Console.WriteLine($"  {utilisateur,-15} {usage}");
        }

        if (c.Personnes.TryGetValue("boulangerie-0", out var etoile))
        {
            Console.WriteLine($"\nTravailleuse à chercher : {etoile.Def.NomComplet}, NISS fictif {etoile.Def.Niss} ({etoile.Def.Langue}), Boulangerie Démo SRL.");
            Console.WriteLine("  Elle a un rendez-vous, une décision signée avec ses documents, et se connecte au portail travailleur avec le compte « travailleur ».");
        }

        Console.WriteLine("\nTravailleurs néerlandophones : Zorg Demo vzw (Gand), décisions signées avec documents en néerlandais.");
        Console.WriteLine("\nParcours :");
        foreach (var ligne in new[]
                 {
                     "P1  CPMT (cpmt) : tableau de bord, rendez-vous du jour, propositions à valider",
                     "P2  recherche d'un travailleur par NISS, dossier travailleur",
                     "P3  planificateur (planificateur) : agenda, créneaux, rendez-vous, convocations",
                     "P4  CPMT : consultation, examen en cours (boulangerie-11), décision en brouillon à signer, document FR ou NL",
                     "P5  employeur (employeur) propose un risque, le CPMT le valide, les obligations sont recalculées",
                     "P6  admin (admin) : paramètres légaux versionnés, risques, modèles de documents",
                     "P7  vaccinations : hépatite B doses 1 et 2, rappel dû (Zorg Demo)",
                     "P8  réintégration : trajet invité, évaluation en attente, délai dépassé",
                     "P9  gestionnaire : correspondance BCE de la Boulangerie, lancer DIMONA en direct",
                     "P10 assistant : salle d'attente ; travailleur : copie du dossier ; dpo : audit",
                     "P11 documents au portail employeur (voir l'état du lot H)",
                 })
        {
            Console.WriteLine($"  {ligne}");
        }

        Console.WriteLine("\nContrôles :");
        foreach (var controle in controles)
        {
            Console.WriteLine($"  [{(controle.Ok ? "OK" : "KO")}] {controle.Parcours} : {controle.Detail}");
        }

        if (c.Avertissements.Count > 0)
        {
            Console.WriteLine($"\n{c.Avertissements.Count} avertissement(s) :");
            foreach (var avertissement in c.Avertissements)
            {
                Console.WriteLine($"  - {avertissement}");
            }
        }

        Console.WriteLine($"\nManifeste : {c.Options.Manifeste}");
    }
}
