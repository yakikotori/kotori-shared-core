namespace Kotori.SharedCore.DomainEvents;

public interface IDomainEventHandler<in T> where T : IDomainEvent
{
    Task HandleAsync(T domainEvent);
}