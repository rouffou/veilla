using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.Pseudonymisation;

// Pseudonymisation d'une copie restaurée des bases pour la recette et la formation (NF-14).
// Usage : Sepp.Pseudonymisation --confirmer <environnement> [--services a,b] [--a-blanc]
// Configuration : voir docs/conformite/pseudonymisation.md (section « Mise en œuvre »).
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args, new Dictionary<string, string>
    {
        ["--confirmer"] = "Pseudonymisation:Confirmation",
        ["--services"] = "Pseudonymisation:Services",
        ["--a-blanc"] = "Pseudonymisation:ABlanc",
    })
    .Build();

try
{
    var rapports = await Execution.ExecuterAsync(configuration, Console.Out, CancellationToken.None);
    return rapports.Count > 0 ? 0 : 1;
}
catch (InvalidOperationException ex)
{
    await Console.Error.WriteLineAsync(ex.Message);
    return 2;
}

namespace Sepp.Pseudonymisation
{
    /// <summary>Environnement hôte minimal pour charger les clés (Development/Test autorisent la clé racine locale).</summary>
    internal sealed class EnvironnementOutil(string nom) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = nom;

        public string ApplicationName { get; set; } = "Sepp.Pseudonymisation";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>Orchestration : garde, chargement des clés, exécution service par service, rapport.</summary>
    public static class Execution
    {
        public static async Task<IReadOnlyList<RapportService>> ExecuterAsync(IConfiguration configuration, TextWriter sortie, CancellationToken ct)
        {
            var racine = configuration.GetSection("Pseudonymisation");
            var environnement = racine["Environnement"];
            var demandes = (racine["Services"] ?? string.Join(',', CatalogueRegles.Tous.Select(s => s.Nom)))
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var regles = demandes.Select(CatalogueRegles.Pour).ToList();
            var cibles = regles.Select(r => new GardeCible.Cible(r.Nom, Connexion(racine, r.Nom))).ToList();

            GardeCible.Verifier(
                environnement,
                cibles,
                racine["Confirmation"],
                racine.GetSection("HotesInterdits").Get<string[]>(),
                racine.GetSection("HotesAutorises").Get<string[]>(),
                Environment.GetEnvironmentVariable("SEPP__ENVIRONNEMENT"));

            var aBlanc = bool.TryParse(racine["ABlanc"], out var v) && v;
            var pseudonymiseur = Pseudonymiseur.DepuisBase64(racine["Cle"]);
            var hote = new EnvironnementOutil(racine["EnvironnementHote"] ?? "Production");
            var rapports = new List<RapportService>();

            foreach (var service in regles)
            {
                var section = racine.GetSection($"Services:{service.Nom}");
                FieldEncryptor? source = null, cible = null;
                BlindIndex? index = null;
                if (service.Chiffrement)
                {
                    source = new FieldEncryptor(ChiffrementChamps.Charger(section.GetSection("Source:Chiffrement"), hote).Cles);
                    var chiffrementCible = ChiffrementChamps.Charger(section.GetSection("Cible:Chiffrement"), hote);
                    cible = new FieldEncryptor(chiffrementCible.Cles);
                    if (service.IndexAveugle)
                    {
                        index = new BlindIndex(chiffrementCible.Desenvelopper(section["Cible:IndexAveugle"], $"{section.Path}:Cible:IndexAveugle"));
                    }
                }

                var rapport = await Moteur.ExecuterAsync(service, Connexion(racine, service.Nom), pseudonymiseur, source, cible, index, environnement!, aBlanc, ct);
                rapports.Add(rapport);
                Afficher(rapport, sortie);
            }

            return rapports;
        }

        private static string Connexion(IConfigurationSection racine, string service) =>
            racine[$"Services:{service}:Connexion"] ?? throw new InvalidOperationException($"Pseudonymisation:Services:{service}:Connexion manquante.");

        private static void Afficher(RapportService rapport, TextWriter sortie)
        {
            sortie.WriteLine($"== {rapport.Service}{(rapport.ABlanc ? " (à blanc : rien n'est écrit)" : string.Empty)}");
            foreach (var (table, nombre) in rapport.LignesParTable)
            {
                sortie.WriteLine($"  {table} : {nombre} ligne(s)");
            }

            sortie.WriteLine($"  tables vidées : {(rapport.TablesVidees.Count == 0 ? "aucune" : string.Join(", ", rapport.TablesVidees))}");
            sortie.WriteLine($"  identifiants d'utilisateur remplacés : {rapport.ValeursUtilisateurRemplacees}");
            sortie.WriteLine($"  colonnes texte sans règle (à revoir) : {rapport.ColonnesNonCouvertes.Count}");
            foreach (var colonne in rapport.ColonnesNonCouvertes)
            {
                sortie.WriteLine($"    - {colonne}");
            }
        }
    }
}
