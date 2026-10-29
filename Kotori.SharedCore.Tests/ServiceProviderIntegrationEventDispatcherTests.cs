using FluentAssertions;
using Kotori.SharedCore.EntityFrameworkCore.Outbox;
using Kotori.SharedCore.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace Kotori.SharedCore.Tests;

public class ServiceProviderIntegrationEventDispatcherTests
{
    private static ServiceProvider CreateServiceProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();

        services.AddSingleton<HandlerCallLog>();
        configure(services);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task DispatchAsync_CallsEveryRegisteredHandlerInOrder_WithDeserializedEvent()
    {
        using var serviceProvider = CreateServiceProvider(services => services
            .AddIntegrationEventHandler<OrderPlaced, FirstOrderPlacedHandler>()
            .AddIntegrationEventHandler<OrderPlaced, SecondOrderPlacedHandler>()
            .AddIntegrationEventHandler<OrderCancelled, OrderCancelledHandler>());

        var serializer = new JsonEventSerializer();
        var original = new OrderPlaced(Guid.NewGuid(), "Book");
        var deserialized = serializer
            .Deserialize(typeof(OrderPlaced).AssemblyQualifiedName!, serializer.Serialize(original))
            .Unwrap();

        var dispatcher = new ServiceProviderIntegrationEventDispatcher(serviceProvider);

        await dispatcher.DispatchAsync(deserialized);

        var calls = serviceProvider.GetRequiredService<HandlerCallLog>().Calls;

        calls.Select(call => call.Handler).Should().Equal(nameof(FirstOrderPlacedHandler), nameof(SecondOrderPlacedHandler));
        calls.Select(call => call.Event).Should().AllBeEquivalentTo(original);
    }

    [Fact]
    public async Task DispatchAsync_Throws_WhenNoHandlerRegistered()
    {
        using var serviceProvider = CreateServiceProvider(services => services
            .AddIntegrationEventHandler<OrderCancelled, OrderCancelledHandler>());

        var dispatcher = new ServiceProviderIntegrationEventDispatcher(serviceProvider);

        var dispatch = () => dispatcher.DispatchAsync(new OrderPlaced(Guid.NewGuid(), "Book"));

        await dispatch.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{typeof(OrderPlaced).FullName}*");
        serviceProvider.GetRequiredService<HandlerCallLog>().Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchAsync_PassesCancellationTokenToHandlers()
    {
        using var serviceProvider = CreateServiceProvider(services => services
            .AddIntegrationEventHandler<OrderCancelled, OrderCancelledHandler>());

        using var cancellationTokenSource = new CancellationTokenSource();
        var dispatcher = new ServiceProviderIntegrationEventDispatcher(serviceProvider);

        await dispatcher.DispatchAsync(new OrderCancelled(Guid.NewGuid()), cancellationTokenSource.Token);

        serviceProvider.GetRequiredService<HandlerCallLog>().Calls
            .Should().ContainSingle()
            .Which.CancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    [Fact]
    public async Task DispatchAsync_CachesDispatchPerEventType()
    {
        using var serviceProvider = CreateServiceProvider(services => services
            .AddIntegrationEventHandler<OrderPlaced, FirstOrderPlacedHandler>()
            .AddIntegrationEventHandler<OrderCancelled, OrderCancelledHandler>());

        var dispatcher = new ServiceProviderIntegrationEventDispatcher(serviceProvider);

        dispatcher.CachedEventTypeCount.Should().Be(0);

        await dispatcher.DispatchAsync(new OrderPlaced(Guid.NewGuid(), "Book"));
        await dispatcher.DispatchAsync(new OrderPlaced(Guid.NewGuid(), "Pen"));

        dispatcher.CachedEventTypeCount.Should().Be(1);

        await dispatcher.DispatchAsync(new OrderCancelled(Guid.NewGuid()));
        await dispatcher.DispatchAsync(new OrderCancelled(Guid.NewGuid()));

        dispatcher.CachedEventTypeCount.Should().Be(2);
        serviceProvider.GetRequiredService<HandlerCallLog>().Calls.Should().HaveCount(4);
    }

    [Fact]
    public async Task AddIntegrationEventHandler_IgnoresDuplicateRegistration()
    {
        using var serviceProvider = CreateServiceProvider(services => services
            .AddIntegrationEventHandler<OrderPlaced, FirstOrderPlacedHandler>()
            .AddIntegrationEventHandler<OrderPlaced, FirstOrderPlacedHandler>());

        var dispatcher = new ServiceProviderIntegrationEventDispatcher(serviceProvider);

        await dispatcher.DispatchAsync(new OrderPlaced(Guid.NewGuid(), "Book"));

        serviceProvider.GetRequiredService<HandlerCallLog>().Calls.Should().ContainSingle();
    }

    public record OrderPlaced(Guid OrderId, string ProductName) : IntegrationEvent;

    public record OrderCancelled(Guid OrderId) : IntegrationEvent;

    public record HandlerCall(string Handler, IIntegrationEvent Event, CancellationToken CancellationToken);

    public class HandlerCallLog
    {
        public List<HandlerCall> Calls { get; } = [];
    }

    public class FirstOrderPlacedHandler : IIntegrationEventHandler<OrderPlaced>
    {
        private readonly HandlerCallLog _log;

        public FirstOrderPlacedHandler(HandlerCallLog log)
        {
            _log = log;
        }

        public Task HandleAsync(OrderPlaced integrationEvent, CancellationToken cancellationToken)
        {
            _log.Calls.Add(new HandlerCall(nameof(FirstOrderPlacedHandler), integrationEvent, cancellationToken));
            return Task.CompletedTask;
        }
    }

    public class SecondOrderPlacedHandler : IIntegrationEventHandler<OrderPlaced>
    {
        private readonly HandlerCallLog _log;

        public SecondOrderPlacedHandler(HandlerCallLog log)
        {
            _log = log;
        }

        public async Task HandleAsync(OrderPlaced integrationEvent, CancellationToken cancellationToken)
        {
            await Task.Yield();
            _log.Calls.Add(new HandlerCall(nameof(SecondOrderPlacedHandler), integrationEvent, cancellationToken));
        }
    }

    public class OrderCancelledHandler : IIntegrationEventHandler<OrderCancelled>
    {
        private readonly HandlerCallLog _log;

        public OrderCancelledHandler(HandlerCallLog log)
        {
            _log = log;
        }

        public Task HandleAsync(OrderCancelled integrationEvent, CancellationToken cancellationToken)
        {
            _log.Calls.Add(new HandlerCall(nameof(OrderCancelledHandler), integrationEvent, cancellationToken));
            return Task.CompletedTask;
        }
    }
}