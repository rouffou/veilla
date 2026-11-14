using System.Text.RegularExpressions;

using Npgsql;

namespace Sepp.Pseudonymisation;

/// <summary>
/// Garde-fou de l'outil : il ne tourne jamais sur une base de production (NF-14). Refus si l'environnement déclaré est la
/// production, si l'environnement du processus (<c>SEPP__ENVIRONNEMENT</c>) l'est, si un hôte ou une base porte un
/// marqueur de production, si un hôte est interdit ou hors de la liste autorisée ; et confirmation obligatoire par
/// ressaisie du nom de l'environnement cible.
/// </summary>
public static partial class GardeCible
{
    private static readonly string[] EnvironnementsInterdits = ["prod", "production", "prd", "live"];

    public sealed record Cible(string Service, string Connexion);

    public static void Verifier(
        string? environnement,
        IEnumerable<Cible> cibles,
        string? confirmation,
        IReadOnlyCollection<string>? hotesInterdits = null,
        IReadOnlyCollection<string>? hotesAutorises = null,
        string? environnementProcessus = null)
    {
        if (string.IsNullOrWhiteSpace(environnement))
        {
            throw new InvalidOperationException("Pseudonymisation:Environnement manquant : indiquer l'environnement cible (recette, formation…).");
        }

        if (EstProduction(environnement) || EstProduction(environnementProcessus))
        {
            throw new InvalidOperationException("Refus : l'outil de pseudonymisation ne tourne jamais sur l'environnement de production.");
        }

        foreach (var cible in cibles)
        {
            var builder = new NpgsqlConnectionStringBuilder(cible.Connexion);
            var hote = builder.Host ?? string.Empty;
            var nomBase = builder.Database ?? string.Empty;
            if (MarqueurProduction().IsMatch(hote) || MarqueurProduction().IsMatch(nomBase))
            {
                throw new InvalidOperationException($"Refus : l'hôte ou la base du service « {cible.Service} » ressemble à la production.");
            }

            if (hotesInterdits?.Any(h => Correspond(hote, h)) == true)
            {
                throw new InvalidOperationException($"Refus : l'hôte du service « {cible.Service} » est interdit (Pseudonymisation:HotesInterdits).");
            }

            if (hotesAutorises is { Count: > 0 } && !hotesAutorises.Any(h => Correspond(hote, h)))
            {
                throw new InvalidOperationException($"Refus : l'hôte du service « {cible.Service} » n'est pas dans Pseudonymisation:HotesAutorises.");
            }
        }

        if (!string.Equals(confirmation?.Trim(), environnement.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Confirmation manquante ou différente : ressaisir le nom de l'environnement cible (« {environnement} »).");
        }
    }

    private static bool EstProduction(string? nom) =>
        nom is not null && EnvironnementsInterdits.Contains(nom.Trim(), StringComparer.OrdinalIgnoreCase);

    private static bool Correspond(string hote, string motif) =>
        motif.StartsWith('*') ? hote.EndsWith(motif[1..], StringComparison.OrdinalIgnoreCase) : string.Equals(hote, motif, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"(^|[^a-z0-9])(prod|production|prd)([^a-z0-9]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarqueurProduction();
}
