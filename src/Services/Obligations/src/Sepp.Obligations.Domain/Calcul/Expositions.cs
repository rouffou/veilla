using Sepp.Obligations.Domain.Projections;

namespace Sepp.Obligations.Domain.Calcul;

/// <summary>
/// Période continue pendant laquelle un travailleur est exposé à un risque chez un affilié, par une ou plusieurs
/// affectations successives (AFF-22). <see cref="Fin"/> est exclusive, <c>null</c> si l'exposition est en cours.
/// Changer de poste en restant exposé au même risque ne crée pas de nouvelle exposition.
/// </summary>
public sealed record Exposition(Guid AffilieId, string CodeRisque, DateOnly Debut, DateOnly? Fin, IReadOnlyList<Guid> PosteIds)
{
    public bool EstActiveAu(DateOnly date) => Debut <= date && (Fin is null || date < Fin);

    /// <summary>L'exposition n'est pas terminée à la date (en cours ou à venir).</summary>
    public bool EstEnCoursOuAVenirAu(DateOnly date) => Fin is null || Fin > date;

    public bool Chevauche(DateOnly debut, DateOnly? finIncluse) =>
        (finIncluse is null || Debut <= finIncluse) && (Fin is null || Fin > debut);
}

/// <summary>
/// Déduit les expositions d'un travailleur de ses affectations et de l'historique des profils de risques des postes
/// (« le système en déduit les risques et les obligations », AFF-22).
/// </summary>
public static class CalculExpositions
{
    public static IReadOnlyList<Exposition> Calculer(IEnumerable<AffectationLocale> affectations, IEnumerable<ProfilRisquePosteLocal> profils)
    {
        var profilsParPoste = profils
            .GroupBy(p => p.PosteId)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.ValideDu).ToList());

        var segments = new List<(Guid Affilie, string Code, DateOnly Debut, DateOnly? Fin, Guid Poste)>();
        foreach (var affectation in affectations)
        {
            if (!profilsParPoste.TryGetValue(affectation.PosteId, out var versions))
            {
                continue;
            }

            for (var i = 0; i < versions.Count; i++)
            {
                var version = versions[i];
                DateOnly? finVersion = i + 1 < versions.Count ? versions[i + 1].ValideDu : null;
                var debut = Max(affectation.DateDebut, version.ValideDu);
                var fin = Min(affectation.DateFin, finVersion);
                if (fin is { } f && f <= debut)
                {
                    continue;
                }

                segments.AddRange(version.CodesRisques.Select(code => (version.AffilieId, code, debut, fin, affectation.PosteId)));
            }
        }

        var expositions = new List<Exposition>();
        foreach (var groupe in segments.GroupBy(s => (s.Affilie, s.Code)))
        {
            var tries = groupe.OrderBy(s => s.Debut).ThenBy(s => s.Fin ?? DateOnly.MaxValue).ToList();
            var debut = tries[0].Debut;
            var fin = tries[0].Fin;
            var postes = new SortedSet<Guid> { tries[0].Poste };
            foreach (var segment in tries.Skip(1))
            {
                if (fin is null || segment.Debut <= fin)
                {
                    fin = fin is null || segment.Fin is null ? null : Max(fin.Value, segment.Fin.Value);
                    postes.Add(segment.Poste);
                    continue;
                }

                expositions.Add(new Exposition(groupe.Key.Affilie, groupe.Key.Code, debut, fin, postes.ToList()));
                debut = segment.Debut;
                fin = segment.Fin;
                postes = [segment.Poste];
            }

            expositions.Add(new Exposition(groupe.Key.Affilie, groupe.Key.Code, debut, fin, postes.ToList()));
        }

        return expositions
            .OrderBy(e => e.AffilieId)
            .ThenBy(e => e.CodeRisque, StringComparer.Ordinal)
            .ThenBy(e => e.Debut)
            .ToList();
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a >= b ? a : b;

    private static DateOnly? Min(DateOnly? a, DateOnly? b) => a is null ? b : b is null ? a : (a < b ? a : b);
}
