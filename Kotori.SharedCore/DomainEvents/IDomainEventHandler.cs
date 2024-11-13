namespace Kotori.SharedCore.DomainEvents;

public interface IDomainEventHandler<T> where T : IDomainEvent
{
    Task HandleAsync(T domainEvent);
}