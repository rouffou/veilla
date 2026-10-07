namespace Sepp.Obligations.Domain.Obligations;

/// <summary>Examen (type et date uniquement) qui a satisfait une obligation.</summary>
public sealed record Realisation(DateOnly Date, Guid ExamenId);

/// <summary>
/// Pourquoi une obligation existe (§15.3 trace_calcul) : règle ou paramètre appliqué, sa version, une explication
/// lisible et les entrées du calcul (dates, fréquences, sources). Aucune donnée médicale.
/// </summary>
public sealed record Justification(string Regle, int? RegleVersion, string Explication, IReadOnlyList<KeyValuePair<string, string>> Entrees)
{
    public bool MemeContenu(Justification autre) =>
        Regle == autre.Regle && RegleVersion == autre.RegleVersion && Explication == autre.Explication && Entrees.SequenceEqual(autre.Entrees);
}

/// <summary>
/// Résultat du moteur d'échéances pour une obligation (SAN-01) : identifiée par une clé stable déduite de son
/// déclencheur (exposition, cycle, événement), de sorte que recalculer ne crée jamais de doublon (SAN-04).
/// </summary>
public sealed record EcheanceCalculee(
    string Cle,
    Guid AffilieId,
    TypeObligation Type,
    OrigineObligation Origine,
    IReadOnlyList<string> CodesRisques,
    DateOnly DateDue,
    DateOnly? DateLimite,
    Realisation? Realisation,
    Justification Justification)
{
    public bool EstRealisee => Realisation is not null;
}
