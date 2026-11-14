using System.Globalization;
using System.Text.Json.Nodes;

namespace Sepp.Outils.DonneesDemo;

/// <summary>Paramètres de la ligne de commande.</summary>
internal sealed record Options(string Hote, string Racine, string Manifeste, int DelaiAttenteSecondes)
{
    public static Options Lire(string[] args)
    {
        var hote = Environment.GetEnvironmentVariable("DEMO_HOTE") ?? "localhost";
        string? racine = null;
        string? manifeste = null;
        var delai = 120;
        for (var i = 0; i < args.Length; i++)
        {
            var suivant = i + 1 < args.Length ? args[i + 1] : throw new EchecDemoException($"Valeur manquante après {args[i]}.");
            switch (args[i])
            {
                case "--hote":
                    hote = suivant;
                    i++;
                    break;
                case "--racine":
                    racine = suivant;
                    i++;
                    break;
                case "--manifeste":
                    manifeste = suivant;
                    i++;
                    break;
                case "--delai":
                    delai = int.Parse(suivant, CultureInfo.InvariantCulture);
                    i++;
                    break;
                default:
                    throw new EchecDemoException($"Option inconnue : {args[i]} (options : --hote, --racine, --manifeste, --delai).");
            }
        }

        racine ??= TrouverRacine();
        return new Options(hote, racine, manifeste ?? Path.Combine(racine, "deploy", "local", "demo", "manifeste.json"), delai);
    }

    private static string TrouverRacine()
    {
        foreach (var depart in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dossier = new DirectoryInfo(depart); dossier is not null; dossier = dossier.Parent)
            {
                if (File.Exists(Path.Combine(dossier.FullName, "Sepp.slnx")))
                {
                    return dossier.FullName;
                }
            }
        }

        throw new EchecDemoException("Racine du dépôt introuvable : lancez l'outil depuis le dépôt ou passez --racine <dossier>.");
    }
}

/// <summary>Un travailleur créé (ou retrouvé) avec ses identifiants.</summary>
internal sealed class PersonneDemo(PersonneDef def)
{
    public PersonneDef Def { get; } = def;

    public Guid Id { get; set; }

    public Guid AffilieId { get; set; }

    public Guid OccupationId { get; set; }

    public Guid? DossierId { get; set; }
}

/// <summary>État partagé entre les étapes : clients, identifiants créés, avertissements et manifeste.</summary>
internal sealed class Contexte
{
    private readonly List<string> _avertissements = [];

    public Contexte(Options options)
    {
        Options = options;
        Jetons = new Jetons(options.Hote);
        Api = new Api(options.Hote, Jetons);
        Keycloak = new KeycloakAdmin(options.Hote);
        Fuseau = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");
        Maintenant = TimeProvider.System.GetUtcNow();
        Aujourdhui = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Maintenant, Fuseau).DateTime);
        Precedent = ChargerManifeste(options.Manifeste);
    }

    public Options Options { get; }

    public Jetons Jetons { get; }

    public Api Api { get; }

    public KeycloakAdmin Keycloak { get; }

    public TimeZoneInfo Fuseau { get; }

    public DateTimeOffset Maintenant { get; }

    public DateOnly Aujourdhui { get; }

    /// <summary>Manifeste de l'exécution précédente (mémoire des objets sans liste dans l'API : examen en cours, brouillon).</summary>
    public JsonNode? Precedent { get; }

    public IReadOnlyList<string> Avertissements => _avertissements;

    public Dictionary<string, Guid> Affilies { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Guid> Sites { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Guid> Postes { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, PersonneDemo> Personnes { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Guid> Lieux { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Guid> Ressources { get; } = new(StringComparer.Ordinal);

    /// <summary>Valeurs libres du manifeste (identifiants et constats), écrites telles quelles.</summary>
    public JsonObject Manifeste { get; } = [];

    /// <summary>Date de début de validité des risques et des profils (jeu d'exemple valable depuis le 1er janvier 2026).</summary>
    public DateOnly DebutProfils => Aujourdhui.AddDays(-180) < new DateOnly(2026, 1, 1) ? new DateOnly(2026, 1, 1) : Aujourdhui.AddDays(-180);

    public void Titre(string texte) => Console.WriteLine($"\n== {texte}");

    public void Info(string texte) => Console.WriteLine($"   {texte}");

    public void Avertir(string texte)
    {
        _avertissements.Add(texte);
        Console.WriteLine($"   ! {texte}");
    }

    /// <summary>Exécute une action isolée : un échec est consigné et la suite continue.</summary>
    public async Task EssayerAsync(string description, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (EchecDemoException ex)
        {
            Avertir($"{description} : {ex.Message}");
        }
        catch (Exception ex) when (ex is NullReferenceException or InvalidOperationException or FormatException or KeyNotFoundException or ArgumentException)
        {
            Avertir($"{description} : erreur inattendue ({ex.GetType().Name}) {ex.Message}");
        }
    }

    /// <summary>Attend une condition en l'interrogeant ; message clair si le délai maximal est dépassé.</summary>
    public async Task<bool> AttendreAsync(string quoi, Func<Task<bool>> condition, int? delaiSecondes = null)
    {
        var delai = TimeSpan.FromSeconds(delaiSecondes ?? Options.DelaiAttenteSecondes);
        var limite = DateTimeOffset.UtcNow + delai;
        Console.Write($"   attente : {quoi} ");
        while (true)
        {
            if (await condition())
            {
                Console.WriteLine("ok");
                return true;
            }

            if (DateTimeOffset.UtcNow >= limite)
            {
                Console.WriteLine();
                Avertir($"Délai de {delai.TotalSeconds:0} s dépassé en attendant : {quoi}. Le flux entre services (bus de développement) est-il actif ? Voir « docker compose logs » du service concerné.");
                return false;
            }

            Console.Write('.');
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    public DateTimeOffset DebutDeJour(DateOnly jour) => new(jour.ToDateTime(TimeOnly.MinValue), Fuseau.GetUtcOffset(jour.ToDateTime(TimeOnly.MinValue)));

    private static JsonNode? ChargerManifeste(string chemin)
    {
        try
        {
            return File.Exists(chemin) ? JsonNode.Parse(File.ReadAllText(chemin)) : null;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
