using System.Globalization;

using Sepp.BuildingBlocks.Domain.Calendar;

using Sepp.Obligations.Domain.Projections;

namespace Sepp.Obligations.Domain.Calcul;

/// <summary>Unité d'un délai légal (mêmes noms que les unités du service Référentiels).</summary>
public enum UniteDuree
{
    JoursOuvrables,
    JoursCalendrier,
    Semaines,
    Mois,
    Annees,
}

/// <summary>
/// Délai ou durée légale applicable à une date : valeur, unité et provenance (paramètre reçu du service Référentiels
/// ou valeur par défaut), reprise telle quelle dans la trace de calcul.
/// </summary>
public sealed record DureeLegale(string Code, int Valeur, UniteDuree Unite, string Source)
{
    /// <summary>Date obtenue en ajoutant la durée ; les jours ouvrables suivent le calendrier belge (DAT-08).</summary>
    public DateOnly AjouterA(DateOnly date, BusinessCalendar calendrier) => Unite switch
    {
        UniteDuree.JoursOuvrables => calendrier.AddBusinessDays(date, Valeur),
        UniteDuree.JoursCalendrier => date.AddDays(Valeur),
        UniteDuree.Semaines => date.AddDays(7 * Valeur),
        UniteDuree.Mois => date.AddMonths(Valeur),
        UniteDuree.Annees => date.AddYears(Valeur),
        _ => throw new InvalidOperationException($"Unité inconnue : {Unite}."),
    };

    public string Description => string.Create(CultureInfo.InvariantCulture, $"{Valeur} {Unite} ({Source})");
}

/// <summary>
/// ARC-21 : politiques légales injectées dans le moteur d'échéances. Aucune valeur légale n'est codée en dur dans le
/// calcul : il demande la durée d'un paramètre à une date.
/// </summary>
public interface IPolitiquesLegales
{
    DureeLegale Duree(string code, DateOnly date);
}

/// <summary>Codes des paramètres légaux du service Référentiels utilisés par le moteur (CatalogueParametresLegaux).</summary>
public static class CodesParametres
{
    public const string RepriseAbsenceMinimum = "SANTE.REPRISE.ABSENCE_MINIMUM";
    public const string RepriseDelai = "SANTE.REPRISE.DELAI";
    public const string ConsultationSpontaneeDelai = "SANTE.CONSULTATION_SPONTANEE.DELAI";
    public const string PreRepriseDelai = "SANTE.PRE_REPRISE.DELAI";
    public const string EstimationPotentiel = "REINTEGRATION.ESTIMATION_POTENTIEL";
    public const string InvitationReintegrationDelai = "REINTEGRATION.INVITATION.DELAI";
    public const string RevueListesNominatives = "SANTE.LISTES_NOMINATIVES.REVUE_ALERTE";
}

/// <summary>Valeur par défaut d'une durée légale (source unique : <c>ParametresLegauxParDefaut</c> du contrat Référentiels).</summary>
public sealed record DureeParDefaut(int Valeur, UniteDuree Unite, string BaseLegale);

/// <summary>
/// Politiques légales alimentées par la projection locale des paramètres du service Référentiels
/// (referentiels.parametre-legal-modifie). Tant qu'aucune valeur n'a été reçue pour une date — Référentiels ne publie
/// qu'à la modification d'un paramètre, pas lors de l'initialisation — la valeur par défaut injectée par la couche
/// application (identique à la valeur initiale du catalogue de Référentiels, réforme du 1er janvier 2026) s'applique ; la
/// trace de calcul indique la provenance. Le domaine ne contient aucune valeur légale.
/// </summary>
public sealed class PolitiquesLegales : IPolitiquesLegales
{
    private readonly IReadOnlyDictionary<string, DureeParDefaut> _defauts;

    private readonly Dictionary<string, List<ParametreLegalLocal>> _valeurs;

    public PolitiquesLegales(IEnumerable<ParametreLegalLocal> valeurs, IReadOnlyDictionary<string, DureeParDefaut> defauts)
    {
        _defauts = defauts;
        _valeurs = valeurs
            .GroupBy(v => v.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.ValideDu).ToList(), StringComparer.Ordinal);
    }

    public DureeLegale Duree(string code, DateOnly date)
    {
        if (_valeurs.TryGetValue(code, out var valeurs))
        {
            var applicable = valeurs.FirstOrDefault(v => v.ValideDu <= date && (v.ValideJusquAu is null || date < v.ValideJusquAu));
            if (applicable is not null && Enum.TryParse<UniteDuree>(applicable.Unite, ignoreCase: true, out var unite) && Enum.IsDefined(unite)
                && applicable.Valeur >= 0 && applicable.Valeur == decimal.Truncate(applicable.Valeur))
            {
                return new DureeLegale(code, (int)applicable.Valeur, unite, string.Create(CultureInfo.InvariantCulture, $"Référentiels, valeur du {applicable.ValideDu:yyyy-MM-dd}"));
            }
        }

        if (!_defauts.TryGetValue(code, out var defaut))
        {
            throw new InvalidOperationException($"Paramètre légal sans valeur par défaut : {code}.");
        }

        return new DureeLegale(code, defaut.Valeur, defaut.Unite, "valeur par défaut");
    }
}

/// <summary>
/// Calendrier des jours ouvrables (DAT-08) : les dix jours fériés légaux belges calculés, plus les jours supplémentaires
/// reçus du service Référentiels (referentiels.jours-feries-modifies).
/// </summary>
public static class CalendrierOuvrable
{
    public static BusinessCalendar Construire(IEnumerable<CalendrierLocal> joursSupplementaires, int anneeDebut, int anneeFin)
    {
        var debut = Math.Max(1900, anneeDebut);
        var fin = Math.Min(2200, anneeFin);
        var legaux = Enumerable.Range(debut, Math.Max(0, fin - debut + 1)).SelectMany(BelgianPublicHolidays.For).Select(h => h.Date);
        return new BusinessCalendar(legaux.Concat(joursSupplementaires.SelectMany(c => c.JoursSupplementaires)));
    }
}
