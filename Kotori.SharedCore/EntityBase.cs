namespace Kotori.SharedCore;

public abstract class EntityBase
{
    public IReadOnlyCollection<IDomainEvent>? RegisteredDomainEvents => _registeredDomainEvents;
    
    private List<IDomainEvent>? _registeredDomainEvents;

    public void RegisterDomainEvent(IDomainEvent domainEvent)
    {
        _registeredDomainEvents ??= [];
        
        _registeredDomainEvents.Add(domainEvent);
    }
}