using Kotori.SharedCore.Exceptions;

namespace Kotori.SharedCore;

public class ServicesDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    public ServicesDomainEventDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task DispatchAsync(IDomainEvent domainEvent)
    {
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
        
        var handlerService = _serviceProvider.GetService(handlerType);

        if (handlerService is null)
        {
            throw new DomainEventHandlerNotFoundException(domainEvent.GetType());
        }
        
        var handleMethod = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
        
        var handleTask = (Task)handleMethod.Invoke(handlerService, [domainEvent])!;

        await handleTask;
    }

    public Task DispatchManyAsync(IEnumerable<IDomainEvent> domainEvents)
        => Task.WhenAll(domainEvents.Select(DispatchAsync));
}