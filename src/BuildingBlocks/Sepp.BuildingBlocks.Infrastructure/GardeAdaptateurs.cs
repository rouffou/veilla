using Microsoft.Extensions.Configuration;

namespace Sepp.BuildingBlocks.Infrastructure;

/// <summary>
/// Garde-fou des adaptateurs externes (ARC-05, NF-14) : hors production, un adaptateur « Reel » encore à l'état de
/// squelette (<see cref="NotImplementedException"/> ou canal non raccordé) fait échouer le démarrage avec un message
/// clair, plutôt que de laisser le service démarrer puis échouer à la première opération. Les simulateurs sont imposés
/// hors production par la configuration de chaque environnement (<c>infra/main.tf</c>, variable <c>adapter_mode</c>).
/// </summary>
public static class GardeAdaptateurs
{
    /// <summary>Valeur de configuration sélectionnant l'adaptateur réel.</summary>
    public const string Reel = "Reel";

    /// <summary>
    /// Environnement courant (<c>SEPP__ENVIRONNEMENT</c>, injecté par Terraform) ; absent en développement local et
    /// en test, ce qui est assimilé à un environnement hors production.
    /// </summary>
    public static bool EstProduction(IConfiguration configuration) =>
        string.Equals(configuration["Sepp:Environnement"], "prod", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Refuse, hors production, le choix « Reel » d'un adaptateur non implémenté.
    /// </summary>
    /// <param name="configuration">Configuration du service.</param>
    /// <param name="cle">Clé de configuration du choix (ex. <c>Integrations:Adaptateurs:Bce</c>).</param>
    /// <param name="choix">Valeur configurée.</param>
    /// <param name="reelImplemente">Vrai si l'adaptateur réel existe réellement (pas un squelette).</param>
    public static void RefuserReelNonImplementeHorsProduction(IConfiguration configuration, string cle, string? choix, bool reelImplemente)
    {
        if (reelImplemente || !string.Equals(choix, Reel, StringComparison.Ordinal) || EstProduction(configuration))
        {
            return;
        }

        throw new InvalidOperationException(
            $"{cle} vaut « {Reel} » mais l'adaptateur réel n'est pas implémenté : hors production " +
            $"(SEPP__ENVIRONNEMENT = « {configuration["Sepp:Environnement"] ?? "non défini"} »), utiliser « Simulateur ».");
    }
}
