using Kotori.SharedCore.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Retry.Simple;

namespace Kotori.SharedCore.Rebus;

public class IntegrationEventLane
{
    private readonly List<Type> _eventTypes = [];
    private readonly List<Type> _forwardedSingletonTypes = [typeof(ILoggerFactory), typeof(IHostApplicationLifetime)];
    private readonly List<Action<IServiceCollection>> _handlerRegistrations = [];

    internal IntegrationEventLane()
    {
    }

    internal IReadOnlyList<Type> EventTypes => _eventTypes;

    internal Type[] ForwardedSingletonTypes => _forwardedSingletonTypes.ToArray();

    public IntegrationEventLane Handle<TEvent, THandler>()
        where TEvent : IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        if (!_eventTypes.Contains(typeof(TEvent)))
        {
            _eventTypes.Add(typeof(TEvent));
            _handlerRegistrations.Add(services => services.AddRebusHandler<IntegrationEventMessageHandler<TEvent>>());
        }

        _handlerRegistrations.Add(services => services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IIntegrationEventHandler<TEvent>, THandler>()));

        return this;
    }

    public IntegrationEventLane Forward<TService>() where TService : notnull
    {
        if (!_forwardedSingletonTypes.Contains(typeof(TService)))
        {
            _forwardedSingletonTypes.Add(typeof(TService));
        }

        return this;
    }

    internal void Register(
        IServiceCollection services,
        Func<RebusConfigurer, RebusConfigurer> configure,
        string errorQueueName,
        IntegrationEventRetryPolicy retryPolicy)
    {
        services.TryAdd(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(Logger<>)));
        services.AddSingleton(retryPolicy);

        foreach (var registration in _handlerRegistrations)
        {
            registration(services);
        }

        services.AddRebus(
            rebus => configure(rebus).Options(options => options.RetryStrategy(
                errorQueueName: errorQueueName,
                maxDeliveryAttempts: retryPolicy.ImmediateAttempts,
                secondLevelRetriesEnabled: true)),
            onCreated: SubscribeAsync);
    }

    private async Task SubscribeAsync(IBus bus)
    {
        foreach (var eventType in _eventTypes)
        {
            await bus.Subscribe(eventType);
        }
    }
}
