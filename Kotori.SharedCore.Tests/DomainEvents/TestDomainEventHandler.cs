using Kotori.SharedCore.DomainEvents;

namespace Kotori.SharedCore.Tests.DomainEvents;

public class TestDomainEventHandler : IDomainEventHandler<TestDomainEvent>
{
    public bool Handled { get; private set; }
    
    public Task HandleAsync(TestDomainEvent domainEvent)
    {
        Handled = true;
        
        return Task.CompletedTask;
    }
}