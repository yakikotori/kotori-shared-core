using System.Collections;

namespace Kotori.SharedCore.DomainEvents;

public class ServiceProviderDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    public ServiceProviderDomainEventDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task DispatchAsync(IDomainEvent domainEvent, DomainEventContext context)
    {
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
        var handlerEnumerableType = typeof(IEnumerable<>).MakeGenericType(handlerType);

        var handlerServices = _serviceProvider.GetService(handlerEnumerableType);
        
        foreach (var handlerService in (IEnumerable)handlerServices!)
        {
            const string handleMethodName = nameof(IDomainEventHandler<IDomainEvent>.HandleAsync);
            
            var handlerMethod = handlerType.GetMethod(handleMethodName)!;
            
            await (Task)handlerMethod.Invoke(handlerService, [domainEvent, context])!;
        }
    }
}