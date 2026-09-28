namespace Sepp.BuildingBlocks.Domain;

/// <summary>
/// Entité identifiée par un identifiant technique UUID v7, généré par le service propriétaire (DAT-01).
/// </summary>
public abstract class Entity
{
    protected Entity(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("L'identifiant d'une entité ne peut pas être vide.");
        }

        Id = id;
    }

    /// <summary>Constructeur réservé à la matérialisation par l'ORM.</summary>
    protected Entity()
    {
    }

    public Guid Id { get; protected init; }

    /// <summary>Nouvel identifiant ordonné dans le temps (UUID v7).</summary>
    protected static Guid NewId() => Guid.CreateVersion7();

    public override bool Equals(object? obj) =>
        obj is Entity other && other.GetType() == GetType() && other.Id == Id;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}

/// <summary>
/// Racine d'agrégat : frontière transactionnelle d'un cas d'usage (ARC-22), collecte ses événements de domaine.
/// </summary>
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(Guid id) : base(id)
    {
    }

    protected AggregateRoot()
    {
    }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>Fait métier survenu dans un agrégat.</summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}

/// <summary>Violation d'une règle métier.</summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }

    public DomainException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public DomainException()
    {
    }
}
