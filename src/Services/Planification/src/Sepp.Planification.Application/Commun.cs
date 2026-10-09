using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Domain.Calendar;
using Sepp.Contracts.Examens;
using Sepp.Planification.Domain;

namespace Sepp.Planification.Application;

/// <summary>Paramétrage du service (section <c>Planification</c>). Les valeurs légales viennent en priorité des paramètres légaux reçus.</summary>
public sealed class OptionsPlanification
{
    /// <summary>SAN-13 : délai du premier rappel (jours calendrier) tant que CONVOCATION.RAPPEL_1 n'a pas été reçu.</summary>
    public int Rappel1Jours { get; set; } = 7;

    /// <summary>SAN-13 : délai du second rappel (jours calendrier) tant que CONVOCATION.RAPPEL_2 n'a pas été reçu.</summary>
    public int Rappel2Jours { get; set; } = 1;

    /// <summary>PLA-06 : délai légal par défaut des urgences (jours ouvrables) tant que le paramètre légal n'a pas été reçu.</summary>
    public int DelaiUrgenceJoursOuvrables { get; set; } = 10;

    /// <summary>
    /// PLA-06 : types d'examen traités comme urgences légales → code du paramètre légal de leur délai. Les codes de type
    /// d'examen sont ceux publiés par le service Obligations (à aligner, voir README).
    /// </summary>
    public Dictionary<string, string> TypesUrgence { get; } = new(StringComparer.Ordinal)
    {
        [TypesExamen.ExamenReprise] = "SANTE.REPRISE.DELAI",
        [TypesExamen.ConsultationSpontanee] = "SANTE.CONSULTATION_SPONTANEE.DELAI",
        [TypesExamen.VisitePreReprise] = "SANTE.PRE_REPRISE.DELAI",
    };

    /// <summary>SAN-11 : types d'acte dont la convocation doit partir en recommandé (par ex. invitations de réintégration).</summary>
    public List<string> TypesRecommandes { get; } = [];

    /// <summary>SAN-10 : canal utilisé quand ni la demande ni l'affilié n'en précisent.</summary>
    public CanalConvocation CanalParDefaut { get; set; } = CanalConvocation.Courrier;

    /// <summary>PLA-06 : un créneau d'urgence encore libre à moins de ce délai s'ouvre aux réservations ordinaires.</summary>
    public TimeSpan LiberationUrgence { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Horizon de recherche d'un créneau de remplacement ou de reconvocation (jours).</summary>
    public int HorizonRechercheJours { get; set; } = 60;

    /// <summary>SAN-03 : une session couvre les obligations dues dans cette fenêtre après sa date (jours).</summary>
    public int FenetreRegroupementJours { get; set; } = 90;

    /// <summary>
    /// Jours fériés supplémentaires (fêtes des Communautés, jours de remplacement) ajoutés aux dix jours fériés légaux
    /// belges pour les délais et la génération des créneaux (DAT-08). Valeur initiale seulement : pour une année dont le
    /// calendrier a été reçu (<c>referentiels.jours-feries-modifies</c>), le calendrier local remplace cette liste.
    /// </summary>
    public List<DateOnly> JoursFeriesSupplementaires { get; } = [];
}

/// <summary>
/// Paramètres légaux applicables (ARC-21) : copie locale reçue de Référentiels, sinon valeur de configuration.
/// Calendrier ouvrable belge (DAT-08) : dix jours fériés légaux et jours supplémentaires configurés.
/// </summary>
public sealed class ParametresPlanification(IParametreLocalRepository parametres, ICalendrierLocalRepository calendriers, OptionsPlanification options)
{
    public const string Rappel1 = "CONVOCATION.RAPPEL_1";
    public const string Rappel2 = "CONVOCATION.RAPPEL_2";

    public OptionsPlanification Options => options;

    public async Task<(int Rappel1, int Rappel2)> DelaisRappelAsync(DateOnly date, CancellationToken cancellationToken) =>
        (await JoursAsync(Rappel1, date, options.Rappel1Jours, cancellationToken),
         await JoursAsync(Rappel2, date, options.Rappel2Jours, cancellationToken));

    /// <summary>Délai légal (jours ouvrables) d'un type d'examen urgent, ou <c>null</c> si le type n'est pas une urgence.</summary>
    public async Task<int?> DelaiUrgenceAsync(string typeExamen, DateOnly date, CancellationToken cancellationToken) =>
        options.TypesUrgence.TryGetValue(typeExamen, out var code)
            ? await JoursAsync(code, date, options.DelaiUrgenceJoursOuvrables, cancellationToken)
            : null;

    public bool EstUrgence(string typeExamen) => options.TypesUrgence.ContainsKey(typeExamen);

    public bool ExigeRecommande(string typeActe) => options.TypesRecommandes.Contains(typeActe, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Calendrier ouvrable belge de la période. Pour chaque année, les jours supplémentaires viennent du calendrier local
    /// reçu de Référentiels s'il existe, sinon de la configuration (valeur initiale).
    /// </summary>
    public async Task<BusinessCalendar> CalendrierAsync(DateOnly du, DateOnly au, CancellationToken cancellationToken)
    {
        var annees = Enumerable.Range(du.Year, Math.Max(1, au.Year - du.Year + 2)).ToArray();
        var locaux = (await calendriers.ListAsync(annees[0], annees[^1], cancellationToken)).ToDictionary(c => c.Annee);
        var supplementaires = annees.SelectMany(a => locaux.TryGetValue(a, out var local)
            ? local.JoursSupplementaires
            : options.JoursFeriesSupplementaires.Where(j => j.Year == a));
        return new BusinessCalendar(annees.SelectMany(BelgianPublicHolidays.For).Select(h => h.Date).Concat(supplementaires));
    }

    private async Task<int> JoursAsync(string code, DateOnly date, int defaut, CancellationToken cancellationToken)
    {
        var parametre = await parametres.ApplicableAsync(code, date, cancellationToken);
        return parametre is null ? defaut : (int)decimal.Truncate(parametre.Valeur);
    }
}

/// <summary>Contrôles d'accès communs : permission de la matrice §3.3 (ARC-41).</summary>
internal static class Acces
{
    public static Error? Exiger(ICurrentUser user, string permission) =>
        user.HasPermission(permission)
            ? null
            : Error.Forbidden("planification.interdit", "Vous n'avez pas le droit d'effectuer cette opération.");

    public static Error PerimetreInterdit() =>
        Error.Forbidden("perimetre.interdit", "Cette personne ou cet affilié ne fait pas partie de votre périmètre.");
}

internal static class Enumerations
{
    /// <summary>Lit une valeur d'énumération sans tenir compte de la casse, des tirets ni des soulignés.</summary>
    public static bool TryParse<TEnum>(string? valeur, out TEnum resultat)
        where TEnum : struct, Enum
    {
        resultat = default;
        if (string.IsNullOrWhiteSpace(valeur))
        {
            return false;
        }

        var normalisee = valeur.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).Trim();
        return !int.TryParse(normalisee, out _) && Enum.TryParse(normalisee, ignoreCase: true, out resultat);
    }

    /// <summary>Valeur facultative : <c>null</c> si absente, erreur de validation si inconnue.</summary>
    public static Result<TEnum?> Optionnel<TEnum>(string? valeur, string nature)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(valeur))
        {
            return Result<TEnum?>.Success(null);
        }

        return TryParse<TEnum>(valeur, out var resultat)
            ? Result<TEnum?>.Success(resultat)
            : Error.Validation("planification.valeur-inconnue",
                $"{nature} inconnu(e) : '{valeur}'. Valeurs admises : {string.Join(", ", Enum.GetNames<TEnum>())}.");
    }
}

internal static class Domaine
{
    /// <summary>Exécute une règle du domaine et traduit sa violation en erreur de validation.</summary>
    public static Error? Essayer(Action action, string code = "planification.regle-metier")
    {
        try
        {
            action();
            return null;
        }
        catch (DomainException ex)
        {
            return Error.Validation(code, ex.Message);
        }
    }

    public static Result<T> Essayer<T>(Func<T> fabrique, string code = "planification.regle-metier")
    {
        try
        {
            return fabrique();
        }
        catch (DomainException ex)
        {
            return Error.Validation(code, ex.Message);
        }
    }
}
