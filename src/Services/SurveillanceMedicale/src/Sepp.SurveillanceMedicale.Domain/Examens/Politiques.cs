using Sepp.BuildingBlocks.Domain;
using Sepp.SurveillanceMedicale.Domain.Protocoles;

namespace Sepp.SurveillanceMedicale.Domain.Examens;

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
