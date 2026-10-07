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

/// <summary>
/// Politiques légales alimentées par la projection locale des paramètres du service Référentiels
/// (referentiels.parametre-legal-modifie). Tant qu'aucune valeur n'a été reçue pour une date — Référentiels ne publie
/// qu'à la modification d'un paramètre, pas lors de l'initialisation — la valeur par défaut documentée ci-dessous,
/// identique à la valeur initiale du catalogue des paramètres légaux de Référentiels (réforme du 1er janvier 2026),
/// s'applique ; la trace de calcul indique la provenance.
/// </summary>
public sealed class PolitiquesLegales : IPolitiquesLegales
{
    /// <summary>Valeurs par défaut (§2.1) : valeur initiale du paramètre dans Référentiels.</summary>
    public static IReadOnlyDictionary<string, (int Valeur, UniteDuree Unite, string BaseLegale)> ValeursParDefaut { get; } =
        new Dictionary<string, (int, UniteDuree, string)>(StringComparer.Ordinal)
        {
            [CodesParametres.RepriseAbsenceMinimum] = (4, UniteDuree.Semaines, "Code du bien-être au travail, livre I, titre 4 (§2.1)"),
            [CodesParametres.RepriseDelai] = (10, UniteDuree.JoursOuvrables, "Code du bien-être au travail, livre I, titre 4 (§2.1)"),
            [CodesParametres.ConsultationSpontaneeDelai] = (10, UniteDuree.JoursOuvrables, "Code du bien-être au travail, livre I, titre 4 (§2.1)"),
            [CodesParametres.PreRepriseDelai] = (10, UniteDuree.JoursOuvrables, "Code du bien-être au travail, livre I, titre 4 (§2.1)"),
            [CodesParametres.EstimationPotentiel] = (8, UniteDuree.Semaines, "Code du bien-être au travail, livre I, titre 4, chapitre 6 (réforme 2026)"),
            [CodesParametres.InvitationReintegrationDelai] = (49, UniteDuree.JoursCalendrier, "Code du bien-être au travail, livre I, titre 4, chapitre 6 (réforme 2026)"),
            [CodesParametres.RevueListesNominatives] = (12, UniteDuree.Mois, "Cahier des charges AFF-32"),
        };

    private readonly Dictionary<string, List<ParametreLegalLocal>> _valeurs;

    public PolitiquesLegales(IEnumerable<ParametreLegalLocal> valeurs)
    {
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

        if (!ValeursParDefaut.TryGetValue(code, out var defaut))
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
