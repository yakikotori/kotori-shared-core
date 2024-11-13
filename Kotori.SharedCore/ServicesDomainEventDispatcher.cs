using System.Collections;

namespace Kotori.SharedCore;

public class ServicesDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    public ServicesDomainEventDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task DispatchAsync(IDomainEvent domainEvent)
    {
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
        var handlerEnumerableType = typeof(IEnumerable<>).MakeGenericType(handlerType);

        var handlerServices = _serviceProvider.GetService(handlerEnumerableType);
        
        foreach (var handlerService in (IEnumerable)handlerServices!)
        {
            const string handleMethodName = nameof(IDomainEventHandler<IDomainEvent>.HandleAsync);
            
            var handlerMethod = handlerType.GetMethod(handleMethodName)!;
            
            _ = (Task)handlerMethod.Invoke(handlerService, [domainEvent])!;
        }
        
        return Task.CompletedTask;
    }

    public Task DispatchManyAsync(IEnumerable<IDomainEvent> domainEvents)
        => Task.WhenAll(domainEvents.Select(DispatchAsync));
}