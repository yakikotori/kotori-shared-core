namespace Kotori.SharedCore.DomainEvents;

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IDomainEvent domainEvent, IUnitOfWork uow);
    
    Task DispatchManyAsync(IEnumerable<IDomainEvent> domainEvents, IUnitOfWork uow);
}