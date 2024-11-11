namespace Kotori.SharedCore;

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IDomainEvent domainEvent);
    
    Task DispatchManyAsync(IEnumerable<IDomainEvent> domainEvents);
}