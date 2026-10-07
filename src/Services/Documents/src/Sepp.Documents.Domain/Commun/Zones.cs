using Sepp.BuildingBlocks.Domain;

namespace Sepp.Documents.Domain.Commun;

/// <summary>
/// Zone de sensibilité d'un modèle et des documents qui en sont issus (ARC-04, §14.3 « contenu chiffré par zone ») :
/// chaque zone a ses propres clés de chiffrement et son conteneur de stockage.
/// </summary>
public enum ZoneDocument
{
    Standard,
    Medicale,
    Psychosociale,
}

public static class Zones
{
    /// <summary>Code de la zone dans les contrats, l'audit et le stockage : <c>standard</c>, <c>medicale</c>, <c>psychosociale</c>.</summary>
    public static string Code(this ZoneDocument zone) => zone switch
    {
        ZoneDocument.Standard => "standard",
        ZoneDocument.Medicale => "medicale",
        ZoneDocument.Psychosociale => "psychosociale",
        _ => throw new DomainException($"Zone inconnue : {zone}."),
    };

    public static ZoneDocument Depuis(string code) => code?.Trim() switch
    {
        "standard" => ZoneDocument.Standard,
        "medicale" => ZoneDocument.Medicale,
        "psychosociale" => ZoneDocument.Psychosociale,
        _ => throw new DomainException($"Zone de document inconnue : '{code}' (standard, medicale ou psychosociale)."),
    };

    public static IReadOnlyList<ZoneDocument> Toutes { get; } = [ZoneDocument.Standard, ZoneDocument.Medicale, ZoneDocument.Psychosociale];
}
