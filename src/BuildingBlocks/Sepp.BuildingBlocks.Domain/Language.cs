namespace Sepp.BuildingBlocks.Domain;

/// <summary>Langues de l'interface et des documents (NF-40) : FR, NL, DE, et EN pour les portails.</summary>
public enum Language
{
    Fr,
    Nl,
    De,
    En,
}

/// <summary>
/// Libellé multilingue (DAT-07). Le français, le néerlandais et l'allemand sont obligatoires ;
/// l'anglais est facultatif (utilisé par les portails) et retombe sur le français.
/// </summary>
public sealed record LocalizedLabel
{
    public LocalizedLabel(string fr, string nl, string de, string? en = null)
    {
        Fr = Require(fr, nameof(fr));
        Nl = Require(nl, nameof(nl));
        De = Require(de, nameof(de));
        En = string.IsNullOrWhiteSpace(en) ? null : en.Trim();
    }

    public string Fr { get; }

    public string Nl { get; }

    public string De { get; }

    public string? En { get; }

    public string In(Language language) => language switch
    {
        Language.Fr => Fr,
        Language.Nl => Nl,
        Language.De => De,
        Language.En => En ?? Fr,
        _ => Fr,
    };

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new DomainException($"Le libellé '{name}' est obligatoire.")
            : value.Trim();
}
