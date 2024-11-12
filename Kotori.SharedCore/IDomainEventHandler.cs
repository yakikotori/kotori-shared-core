namespace Kotori.SharedCore;

public interface IDomainEventHandler<T> where T : IDomainEvent
{
    Task HandleAsync(T domainEvent);
}