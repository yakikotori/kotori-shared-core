namespace Kotori.SharedCore.DomainEvents;

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IDomainEvent domainEvent, DomainEventContext context);
}