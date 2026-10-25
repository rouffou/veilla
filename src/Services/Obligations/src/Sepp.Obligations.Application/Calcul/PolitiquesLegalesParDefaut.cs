using Sepp.Contracts.Referentiels;
using Sepp.Obligations.Domain.Calcul;

namespace Sepp.Obligations.Application.Calcul;

/// <summary>
/// ARC-21 : valeurs par défaut des paramètres légaux utilisés par le moteur d'échéances, lues dans la source unique
/// <see cref="ParametresLegauxParDefaut"/> (contrat Référentiels) — aucune valeur n'est redéclarée dans Obligations.
/// </summary>
public static class PolitiquesLegalesParDefaut
{
    private static readonly string[] CodesUtilises =
    [
        CodesParametres.RepriseAbsenceMinimum,
        CodesParametres.RepriseDelai,
        CodesParametres.ConsultationSpontaneeDelai,
        CodesParametres.PreRepriseDelai,
        CodesParametres.EstimationPotentiel,
        CodesParametres.InvitationReintegrationDelai,
        CodesParametres.RevueListesNominatives,
    ];

    /// <summary>Codes des paramètres suivis par le service (les autres événements de paramètre sont ignorés).</summary>
    public static IReadOnlyDictionary<string, DureeParDefaut> Defauts { get; } = CodesUtilises.ToDictionary(
        code => code,
        code =>
        {
            var valeur = ParametresLegauxParDefaut.Obtenir(code);
            return new DureeParDefaut(valeur.Valeur, Enum.Parse<UniteDuree>(valeur.Unite), valeur.BaseLegale);
        },
        StringComparer.Ordinal);
}
