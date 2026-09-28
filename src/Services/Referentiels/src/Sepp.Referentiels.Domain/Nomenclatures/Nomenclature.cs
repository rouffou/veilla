using Sepp.BuildingBlocks.Domain;

namespace Sepp.Referentiels.Domain.Nomenclatures;

/// <summary>
/// Nomenclature partagée (NACE, commissions paritaires, métiers types, catégories de risques…),
/// versionnée, avec libellés en français, néerlandais, allemand et anglais (DAT-07).
/// </summary>
public sealed class Nomenclature : AggregateRoot
{
    private readonly List<EntreeNomenclature> _entrees = [];

    private Nomenclature()
    {
    }

    private Nomenclature(Guid id, string code, LocalizedLabel libelle) : base(id)
    {
        Code = code;
        Libelle = libelle;
        NumeroVersion = 1;
    }

    public string Code { get; private set; } = string.Empty;

    public LocalizedLabel Libelle { get; private set; } = null!;

    /// <summary>Version du contenu, incrémentée à chaque modification.</summary>
    public int NumeroVersion { get; private set; }

    public IReadOnlyList<EntreeNomenclature> Entrees => _entrees.AsReadOnly();

    public static Nomenclature Creer(string code, LocalizedLabel libelle)
    {
        var normalized = NormaliserCode(code);
        var nomenclature = new Nomenclature(NewId(), normalized, libelle);
        nomenclature.Raise(new NomenclatureVersionnee(nomenclature.Id, normalized, 1, DateTimeOffset.UtcNow));
        return nomenclature;
    }

    /// <summary>Entrées valides à une date donnée.</summary>
    public IEnumerable<EntreeNomenclature> EntreesAu(DateOnly date) =>
        _entrees.Where(e => e.Validite.Contains(date)).OrderBy(e => e.Code, StringComparer.Ordinal);

    /// <summary>Ajoute un code ; il ne doit pas déjà être valide sur une période qui chevauche.</summary>
    public void AjouterEntree(string code, LocalizedLabel libelle, DateOnly valideDu, string? codeParent = null)
    {
        var normalized = NormaliserCode(code);
        var validite = new Validity(valideDu);
        if (_entrees.Any(e => e.Code == normalized && e.Validite.Overlaps(validite)))
        {
            throw new DomainException($"Le code '{normalized}' existe déjà dans la nomenclature {Code} sur cette période.");
        }

        if (codeParent is not null && !_entrees.Any(e => e.Code == NormaliserCode(codeParent)))
        {
            throw new DomainException($"Le code parent '{codeParent}' est inconnu dans la nomenclature {Code}.");
        }

        _entrees.Add(new EntreeNomenclature(NewId(), normalized, libelle, validite, codeParent is null ? null : NormaliserCode(codeParent)));
        IncrementerVersion();
    }

    /// <summary>Modifie le libellé d'un code à partir d'une date : l'entrée courante est clôturée, une nouvelle est créée (DAT-04).</summary>
    public void ModifierLibelle(string code, LocalizedLabel libelle, DateOnly aPartirDu)
    {
        var courante = EntreeOuverte(code);
        if (aPartirDu <= courante.Validite.ValidFrom)
        {
            throw new DomainException($"La modification du code '{courante.Code}' doit prendre effet après le {courante.Validite.ValidFrom:yyyy-MM-dd}.");
        }

        courante.Cloturer(aPartirDu);
        _entrees.Add(new EntreeNomenclature(NewId(), courante.Code, libelle, new Validity(aPartirDu), courante.CodeParent));
        IncrementerVersion();
    }

    /// <summary>Retire un code à partir d'une date (il reste consultable pour l'historique).</summary>
    public void RetirerEntree(string code, DateOnly aPartirDu)
    {
        var courante = EntreeOuverte(code);
        if (aPartirDu <= courante.Validite.ValidFrom)
        {
            throw new DomainException($"Le retrait du code '{courante.Code}' doit prendre effet après le {courante.Validite.ValidFrom:yyyy-MM-dd}.");
        }

        courante.Cloturer(aPartirDu);
        IncrementerVersion();
    }

    private EntreeNomenclature EntreeOuverte(string code)
    {
        var normalized = NormaliserCode(code);
        return _entrees.SingleOrDefault(e => e.Code == normalized && e.Validite.IsOpen)
               ?? throw new DomainException($"Aucune entrée en vigueur pour le code '{normalized}' dans la nomenclature {Code}.");
    }

    private void IncrementerVersion()
    {
        NumeroVersion++;
        Raise(new NomenclatureVersionnee(Id, Code, NumeroVersion, DateTimeOffset.UtcNow));
    }

    private static string NormaliserCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 50)
        {
            throw new DomainException("Un code de nomenclature est obligatoire (50 caractères maximum).");
        }

        return code.Trim().ToUpperInvariant();
    }
}

public sealed class EntreeNomenclature : Entity
{
    private EntreeNomenclature()
    {
    }

    internal EntreeNomenclature(Guid id, string code, LocalizedLabel libelle, Validity validite, string? codeParent) : base(id)
    {
        Code = code;
        Libelle = libelle;
        Validite = validite;
        CodeParent = codeParent;
    }

    public string Code { get; private set; } = string.Empty;

    public LocalizedLabel Libelle { get; private set; } = null!;

    public Validity Validite { get; private set; }

    /// <summary>Hiérarchie facultative (par ex. section → division NACE).</summary>
    public string? CodeParent { get; private set; }

    internal void Cloturer(DateOnly fin) => Validite = Validite.CloseAt(fin);
}

public sealed record NomenclatureVersionnee(Guid NomenclatureId, string Code, int Version, DateTimeOffset OccurredAt) : IDomainEvent;
