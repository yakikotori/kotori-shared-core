using System.Collections.Concurrent;
using System.Reflection;

namespace Kotori.SharedCore.IntegrationEvents;

public class ServiceProviderIntegrationEventDispatcher : IIntegrationEventDispatcher
{
    private static readonly MethodInfo DispatchToHandlersMethod = typeof(ServiceProviderIntegrationEventDispatcher)
        .GetMethod(nameof(DispatchToHandlersAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<Type, Func<IServiceProvider, IIntegrationEvent, CancellationToken, Task>> _dispatchers = new();

    public ServiceProviderIntegrationEventDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    internal int CachedEventTypeCount => _dispatchers.Count;

    public Task DispatchAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var dispatcher = _dispatchers.GetOrAdd(integrationEvent.GetType(), CreateDispatcher);

        return dispatcher(_serviceProvider, integrationEvent, cancellationToken);
    }

    private static Func<IServiceProvider, IIntegrationEvent, CancellationToken, Task> CreateDispatcher(Type eventType)
        => DispatchToHandlersMethod
            .MakeGenericMethod(eventType)
            .CreateDelegate<Func<IServiceProvider, IIntegrationEvent, CancellationToken, Task>>();

    private static async Task DispatchToHandlersAsync<TEvent>(
        IServiceProvider serviceProvider,
        IIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) where TEvent : IIntegrationEvent
    {
        var handlers = serviceProvider.GetService(typeof(IEnumerable<IIntegrationEventHandler<TEvent>>))
            as IEnumerable<IIntegrationEventHandler<TEvent>>;

        var handlerArray = handlers?.ToArray() ?? [];

        if (handlerArray.Length == 0)
        {
            throw new InvalidOperationException(
                $"No integration event handler is registered for {typeof(TEvent).FullName}");
        }

        foreach (var handler in handlerArray)
        {
            await handler.HandleAsync((TEvent)integrationEvent, cancellationToken);
        }
    }
}