using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Domain.Calendar;
using Sepp.SurveillanceMedicale.Domain.Protocoles;

namespace Sepp.SurveillanceMedicale.Domain.Examens;

/// <summary>
/// Politique légale de l'examen de reprise (§2.1, ARC-21) : obligatoire après une absence d'au moins 4 semaines,
/// entre le jour de la reprise et 10 jours ouvrables après (paramètre <c>SANTE.REPRISE.DELAI</c> du service
/// Référentiels), calculé avec le calendrier belge des jours ouvrables (DAT-08).
/// </summary>
public sealed class PolitiqueDelaiReprise
{
    public const string CodeParametreDelai = "SANTE.REPRISE.DELAI";
    public const int DelaiParDefaut = 10;

    private readonly BusinessCalendar _calendrier;

    public PolitiqueDelaiReprise(int delaiJoursOuvrables, BusinessCalendar calendrier)
    {
        if (delaiJoursOuvrables < 1)
        {
            throw new DomainException("Le délai de l'examen de reprise est d'au moins un jour ouvrable.");
        }

        DelaiJoursOuvrables = delaiJoursOuvrables;
        _calendrier = calendrier;
    }

    public int DelaiJoursOuvrables { get; }

    public DateOnly DateLimite(DateOnly dateReprise) => _calendrier.AddBusinessDays(dateReprise, DelaiJoursOuvrables);

    public bool EstRespecte(DateOnly dateReprise, DateOnly dateExamen) =>
        dateExamen >= dateReprise && dateExamen <= DateLimite(dateReprise);
}

/// <summary>
/// SAN-23 : comparaison des mesures aux valeurs de référence en vigueur à la date de l'acte. Une mesure hors de
/// l'intervalle [min, max] est « inhabituelle » ; une mesure sans référence n'est jamais signalée.
/// </summary>
public static class EvaluateurResultats
{
    public static IReadOnlyList<MesureEvaluee> Evaluer(TypeActe typeActe, IEnumerable<Mesure> mesures, IEnumerable<ValeurReference> references, DateOnly date)
    {
        var applicables = references.Where(r => r.TypeActe == typeActe && r.Validite.Contains(date)).ToList();
        return
        [
            .. mesures.Select(m =>
            {
                var code = Garde.Code(m.Code, "mesure");
                if (m.Valeur is null && string.IsNullOrWhiteSpace(m.Texte))
                {
                    throw new DomainException($"La mesure {code} n'a ni valeur ni texte.");
                }

                var reference = applicables.Find(r => r.CodeMesure == code);
                var inhabituelle = m.Valeur is { } v && reference is not null && !reference.EstNormale(v);
                return new MesureEvaluee(
                    code, m.Valeur, Garde.Facultatif(m.Unite, "unité", 20) ?? reference?.Unite, Garde.Facultatif(m.Texte, "texte", 2_000),
                    reference?.Minimum, reference?.Maximum, inhabituelle);
            }),
        ];
    }
}
