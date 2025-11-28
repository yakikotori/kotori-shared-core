using System.Collections;

namespace Kotori.SharedCore.DomainEvents;

public class ServiceProviderDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    public ServiceProviderDomainEventDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task DispatchAsync(IDomainEvent domainEvent, IUnitOfWork uow)
    {
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
        var handlerEnumerableType = typeof(IEnumerable<>).MakeGenericType(handlerType);

        var handlerServices = _serviceProvider.GetService(handlerEnumerableType);
        
        foreach (var handlerService in (IEnumerable)handlerServices!)
        {
            const string handleMethodName = nameof(IDomainEventHandler<IDomainEvent>.HandleAsync);
            
            var handlerMethod = handlerType.GetMethod(handleMethodName)!;
            
            _ = (Task)handlerMethod.Invoke(handlerService, [domainEvent, uow])!;
        }
        
        return Task.CompletedTask;
    }

    public Task DispatchManyAsync(IEnumerable<IDomainEvent> domainEvents, IUnitOfWork uow)
        => Task.WhenAll(domainEvents.Select(domainEvent => DispatchAsync(domainEvent, uow)));
}