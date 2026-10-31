using Kotori.SharedCore.Inbox;
using Kotori.SharedCore.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Retry.Simple;
using Rebus.Transport;

namespace Kotori.SharedCore.Rebus;

public class IntegrationEventLane
{
    private readonly List<Type> _eventTypes = [];
    private readonly List<Type> _forwardedSingletonTypes =
    [
        typeof(ILoggerFactory),
        typeof(IHostApplicationLifetime),
        typeof(IIntegrationEventLaneMetrics)
    ];
    private readonly List<Action<IServiceCollection>> _handlerRegistrations = [];

    private Action<IServiceCollection> _inboxRegistration = services => services.AddSingleton<IInbox, NoInbox>();

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

    public IntegrationEventLane UseInbox()
    {
        _inboxRegistration = _ => { };

        return Forward<IInbox>();
    }

    internal void Register(
        IServiceCollection services,
        Func<RebusConfigurer, RebusConfigurer> configure,
        string errorQueueName,
        IntegrationEventRetryPolicy retryPolicy)
    {
        services.TryAdd(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(Logger<>)));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(retryPolicy);

        foreach (var registration in _handlerRegistrations)
        {
            registration(services);
        }

        _inboxRegistration(services);

        var inputQueue = string.Empty;

        services.AddSingleton(_ => new IntegrationEventLaneConsumer(inputQueue));

        services.AddRebus(
            rebus => configure(rebus).Options(options =>
            {
                options.RetryStrategy(
                    errorQueueName: errorQueueName,
                    maxDeliveryAttempts: retryPolicy.ImmediateAttempts,
                    secondLevelRetriesEnabled: true);
                options.Decorate(context =>
                {
                    var transport = context.Get<ITransport>();
                    inputQueue = transport.Address;
                    return transport;
                });
            }),
            onCreated: (bus, provider) => ResolveHandlersAndSubscribeAsync(bus, provider, inputQueue));
    }

    private async Task ResolveHandlersAndSubscribeAsync(IBus bus, IServiceProvider provider, string inputQueue)
    {
        ResolveInbox(provider, inputQueue);
        ResolveMetrics(provider, inputQueue);

        foreach (var eventType in _eventTypes)
        {
            ResolveHandlers(provider, inputQueue, eventType);
        }

        foreach (var eventType in _eventTypes)
        {
            await bus.Subscribe(eventType);
        }
    }

    private static void ResolveInbox(IServiceProvider provider, string inputQueue)
    {
        try
        {
            provider.GetRequiredService<IInbox>();
            provider.GetRequiredService<IntegrationEventLaneConsumer>();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Integration event lane {inputQueue} cannot resolve its inbox: {exception.Message}",
                exception);
        }
    }

    private static void ResolveMetrics(IServiceProvider provider, string inputQueue)
    {
        try
        {
            provider.GetRequiredService<IIntegrationEventLaneMetrics>();
            provider.GetRequiredService<TimeProvider>();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Integration event lane {inputQueue} cannot resolve its metrics: {exception.Message}",
                exception);
        }
    }

    private static void ResolveHandlers(IServiceProvider provider, string inputQueue, Type eventType)
    {
        try
        {
            provider.GetRequiredService<IntegrationEventRetryPolicy>();
            provider.GetServices(typeof(IIntegrationEventHandler<>).MakeGenericType(eventType)).ToArray();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Integration event lane {inputQueue} cannot resolve the handlers of {eventType}: {exception.Message}",
                exception);
        }
    }
}
