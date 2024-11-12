using System.Collections;
using Kotori.SharedCore.Exceptions;

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

        var handlers = _serviceProvider.GetService(handlerEnumerableType);
        
        if (handlers is null)
        {
            throw new DomainEventHandlerNotFoundException(domainEvent.GetType());
        }
        
        foreach (var handler in (IEnumerable)handlers)
        {
            var handlerMethod = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
            
            _ = (Task)handlerMethod.Invoke(handler, [domainEvent])!;
        }
        
        return Task.CompletedTask;
    }

    public Task DispatchManyAsync(IEnumerable<IDomainEvent> domainEvents)
        => Task.WhenAll(domainEvents.Select(DispatchAsync));
}