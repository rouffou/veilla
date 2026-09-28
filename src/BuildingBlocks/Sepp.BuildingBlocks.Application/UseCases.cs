using Sepp.Contracts;

namespace Sepp.BuildingBlocks.Application;

/// <summary>Cas d'usage en écriture : une commande = une transaction (ARC-22).</summary>
public interface ICommandHandler<in TCommand, TResult>
{
    Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

/// <summary>Cas d'usage en lecture.</summary>
public interface IQueryHandler<in TQuery, TResult>
{
    Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken);
}

/// <summary>Valide la transaction du cas d'usage : données et événements de l'outbox ensemble (ARC-32).</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Événements d'intégration à publier, écrits dans l'outbox lors du <see cref="IUnitOfWork.SaveChangesAsync"/>.</summary>
public interface IIntegrationEventOutbox
{
    void Add(IntegrationEvent integrationEvent);
}

/// <summary>Consommateur d'un événement d'intégration ; l'idempotence est garantie par l'inbox (ARC-31).</summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}
