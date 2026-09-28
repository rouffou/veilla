using Sepp.BuildingBlocks.Domain;

namespace Sepp.Audit.Domain.Journal;

/// <summary>
/// Zone de sensibilité (ARC-04). Chaque zone a sa propre chaîne d'empreintes et son propre périmètre de
/// consultation (PSY-20 : le CPAP dirigeant ne voit que la zone psychosociale).
/// </summary>
public enum Zone
{
    Standard,
    Medicale,
    Psychosociale,
}

/// <summary>Nature de l'accès journalisé (NF-04).</summary>
public enum ActionAudit
{
    Lecture,
    Creation,
    Modification,
    Suppression,
    Export,
}

/// <summary>Codes échangés dans les contrats et l'API : minuscules, sans accent (<c>medicale</c>, <c>lecture</c>…).</summary>
public static class CodesAudit
{
    public static string Code(this Zone zone) => zone.ToString().ToLowerInvariant();

    public static string Code(this ActionAudit action) => action.ToString().ToLowerInvariant();

    public static bool TryParse<TEnum>(string? code, out TEnum valeur)
        where TEnum : struct, Enum
    {
        valeur = default;
        return !string.IsNullOrWhiteSpace(code)
            && char.IsLetter(code.Trim()[0])
            && Enum.TryParse(code.Trim(), ignoreCase: true, out valeur)
            && Enum.IsDefined(valeur);
    }

    public static TEnum Parse<TEnum>(string? code)
        where TEnum : struct, Enum =>
        TryParse<TEnum>(code, out var valeur)
            ? valeur
            : throw new DomainException($"Code {typeof(TEnum).Name} inconnu : '{code}'.");
}
