using Sepp.BuildingBlocks.Domain;

namespace Sepp.Referentiels.Domain.Parametres;

/// <summary>Unité d'un paramètre légal.</summary>
public enum UniteParametre
{
    JoursOuvrables,
    JoursCalendrier,
    Semaines,
    Mois,
    Annees,
    Nombre,
}

/// <summary>
/// Règle légale paramétrable (délai, fréquence, durée de conservation, seuil) : la réglementation évolue,
/// aucune valeur légale n'est codée en dur dans les services (§2, ARC-21, NF-61).
/// Chaque valeur a une période de validité ; une nouvelle valeur clôture la précédente (DAT-04).
/// </summary>
public sealed class ParametreLegal : AggregateRoot
{
    private readonly List<ValeurParametre> _valeurs = [];

    private ParametreLegal()
    {
    }

    private ParametreLegal(Guid id, CodeParametre code, LocalizedLabel libelle, UniteParametre unite, string baseLegale)
        : base(id)
    {
        Code = code;
        Libelle = libelle;
        Unite = unite;
        BaseLegale = baseLegale;
    }

    public CodeParametre Code { get; private set; } = null!;

    public LocalizedLabel Libelle { get; private set; } = null!;

    public UniteParametre Unite { get; private set; }

    /// <summary>Référence du texte légal (article, arrêté).</summary>
    public string BaseLegale { get; private set; } = string.Empty;

    public IReadOnlyList<ValeurParametre> Valeurs => _valeurs.AsReadOnly();

    public static ParametreLegal Creer(CodeParametre code, LocalizedLabel libelle, UniteParametre unite, string baseLegale, decimal valeur, DateOnly valideDu)
    {
        var parametre = new ParametreLegal(NewId(), code, libelle, unite, baseLegale.Trim());
        parametre.DefinirValeur(valeur, valideDu);
        return parametre;
    }

    /// <summary>Valeur applicable à une date, ou <c>null</c> si le paramètre n'est pas encore en vigueur.</summary>
    public decimal? ValeurAu(DateOnly date) =>
        _valeurs.FirstOrDefault(v => v.Validite.Contains(date))?.Valeur;

    /// <summary>
    /// Définit une nouvelle valeur à partir d'une date. La valeur en cours est clôturée à cette date.
    /// Les valeurs passées ne sont jamais réécrites : on ne peut pas antidater avant la dernière valeur.
    /// </summary>
    public void DefinirValeur(decimal valeur, DateOnly valideDu)
    {
        if (valeur < 0)
        {
            throw new DomainException($"La valeur du paramètre {Code} ne peut pas être négative.");
        }

        if (Unite != UniteParametre.Nombre && valeur != decimal.Truncate(valeur))
        {
            throw new DomainException($"La valeur du paramètre {Code} doit être un nombre entier de {Unite}.");
        }

        var courante = _valeurs.SingleOrDefault(v => v.Validite.IsOpen);
        if (courante is not null)
        {
            if (valideDu <= courante.Validite.ValidFrom)
            {
                throw new DomainException(
                    $"La nouvelle valeur du paramètre {Code} doit prendre effet après le {courante.Validite.ValidFrom:yyyy-MM-dd}.");
            }

            courante.Cloturer(valideDu);
        }

        _valeurs.Add(new ValeurParametre(NewId(), valeur, new Validity(valideDu)));
        Raise(new ValeurParametreDefinie(Code, valeur, Unite, valideDu, DateTimeOffset.UtcNow));
    }
}

public sealed class ValeurParametre : Entity
{
    private ValeurParametre()
    {
    }

    internal ValeurParametre(Guid id, decimal valeur, Validity validite) : base(id)
    {
        Valeur = valeur;
        Validite = validite;
    }

    public decimal Valeur { get; private set; }

    public Validity Validite { get; private set; }

    internal void Cloturer(DateOnly fin) => Validite = Validite.CloseAt(fin);
}

/// <summary>Code stable d'un paramètre, en majuscules et points : <c>SANTE.REPRISE.DELAI</c>.</summary>
public sealed record CodeParametre
{
    public CodeParametre(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100 ||
            !value.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c is '.' or '_'))
        {
            throw new DomainException($"Code de paramètre invalide : '{value}'. Attendu : majuscules, chiffres, '.' et '_'.");
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record ValeurParametreDefinie(
    CodeParametre Code,
    decimal Valeur,
    UniteParametre Unite,
    DateOnly ValideDu,
    DateTimeOffset OccurredAt) : IDomainEvent;
