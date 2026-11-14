using Microsoft.Extensions.Configuration;

namespace Sepp.BuildingBlocks.Infrastructure;

/// <summary>
/// Garde-fou des adaptateurs externes (ARC-05, NF-14) : hors production, un adaptateur « Reel » fait échouer le
/// démarrage avec un message clair. Les simulateurs sont imposés hors production par la configuration de chaque
/// environnement (<c>infra/main.tf</c>, variable <c>adapter_mode</c>) ; ce garde-fou en est la seconde ligne de défense,
/// côté service, contre une configuration manuelle ou une variable d'environnement égarée.
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
    /// Refuse, hors production, le choix « Reel » d'un adaptateur, qu'il soit implémenté ou non (NF-14) : tout
    /// environnement autre que la production (recette, formation, préproduction, développement, environnement non défini)
    /// travaille sur des simulateurs, pour ne jamais joindre un système externe réel (BCE, Dimona, eHealth, e-mail…) avec
    /// des données de recette ou de formation. Aucune dérogation n'existe : ni le cahier des charges ni les ADR n'en
    /// prévoient, et Terraform refuse déjà <c>adapter_mode = Reel</c> hors production (<c>infra/variables.tf</c>).
    /// </summary>
    /// <param name="configuration">Configuration du service.</param>
    /// <param name="cle">Clé de configuration du choix (ex. <c>Integrations:Adaptateurs:Bce</c>).</param>
    /// <param name="choix">Valeur configurée.</param>
    public static void RefuserReelHorsProduction(IConfiguration configuration, string cle, string? choix)
    {
        if (!string.Equals(choix, Reel, StringComparison.Ordinal) || EstProduction(configuration))
        {
            return;
        }

        throw new InvalidOperationException(
            $"{cle} vaut « {Reel} » hors production (SEPP__ENVIRONNEMENT = « {configuration["Sepp:Environnement"] ?? "non défini"} ») : " +
            "les adaptateurs réels sont réservés à la production (NF-14), utiliser « Simulateur ».");
    }
}
