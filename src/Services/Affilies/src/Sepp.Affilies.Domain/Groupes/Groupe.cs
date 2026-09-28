using Sepp.BuildingBlocks.Domain;

namespace Sepp.Affilies.Domain.Groupes;

/// <summary>Groupe d'entreprises : regroupement facultatif d'affiliés (AFF-02, §15.3).</summary>
public sealed class Groupe : AggregateRoot
{
    private Groupe()
    {
    }

    private Groupe(Guid id, string nom) : base(id) => Nom = nom;

    public string Nom { get; private set; } = string.Empty;

    public static Groupe Creer(string nom) => new(NewId(), Normaliser(nom));

    public void Renommer(string nom)
    {
        Nom = Normaliser(nom);
        Raise(new GroupeRenomme(Id, DateTimeOffset.UtcNow));
    }

    private static string Normaliser(string nom) =>
        string.IsNullOrWhiteSpace(nom) || nom.Trim().Length > 200
            ? throw new DomainException("Le nom du groupe est obligatoire (200 caractères maximum).")
            : nom.Trim();
}

public sealed record GroupeRenomme(Guid GroupeId, DateTimeOffset OccurredAt) : IDomainEvent;
