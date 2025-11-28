namespace Kotori.SharedCore.DomainEvents;

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IDomainEvent domainEvent);
    
    Task DispatchManyAsync(IEnumerable<IDomainEvent> domainEvents);
}