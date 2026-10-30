using FluentAssertions;
using Kotori.SharedCore.IntegrationEvents;
using Kotori.SharedCore.Rebus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rebus.Config;
using Rebus.Messages;
using Rebus.Persistence.InMem;
using Rebus.Transport.InMem;

namespace Kotori.SharedCore.Tests;

public class RebusIntegrationEventLaneTests : IAsyncLifetime
{
    private const string ErrorQueue = "lanes.error";

    private readonly InMemNetwork _network = new();
    private readonly IntegrationEventRetryPolicy _quickRetry = IntegrationEventRetryPolicy
        .Create(2, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3))
        .Unwrap();

    private IHost _host = null!;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    private async Task StartAsync(Action<IHostBuilder> addLanes)
    {
        var builder = new HostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton<LaneCallLog>();
            services.AddSingleton<LabelPrinter>();
            services.AddRebus(rebus => rebus.Transport(transport => transport.UseInMemoryTransportAsOneWayClient(_network)));
            services.AddRebusIntegrationEventDispatcher();
        });

        addLanes(builder);

        _host = builder.Build();

        await _host.StartAsync();
    }

    private Func<RebusConfigurer, RebusConfigurer> InMemoryLane(string queue)
        => rebus => rebus
            .Transport(transport => transport.UseInMemoryTransport(_network, queue))
            .Timeouts(timeouts => timeouts.StoreInMemory());

    private Task DispatchAsync(IIntegrationEvent integrationEvent)
        => _host.Services.GetRequiredService<IIntegrationEventDispatcher>().DispatchAsync(integrationEvent);

    private LaneCallLog CallLog => _host.Services.GetRequiredService<LaneCallLog>();

    [Fact]
    public async Task LaneRunsEveryHandlerOfTheEventInOrderWithTheMessageCancellationToken()
    {
        await StartAsync(host => host.AddIntegrationEventLane(InMemoryLane("lane-a"), ErrorQueue, _quickRetry, lane => lane
            .Handle<CrateShipped, LaneAShippedHandler>()
            .Handle<CrateShipped, LaneAShippedAuditHandler>()
            .Forward<LaneCallLog>()));

        var shipped = new CrateShipped(Guid.NewGuid());

        await DispatchAsync(shipped);
        await CallLog.WaitForAsync(2);

        CallLog.Calls.Select(call => call.Handler).Should().Equal(nameof(LaneAShippedHandler), nameof(LaneAShippedAuditHandler));
        CallLog.Calls.Should().AllSatisfy(call =>
        {
            call.Event.Should().Be(shipped);
            call.CanBeCancelled.Should().BeTrue();
        });
    }

    [Fact]
    public async Task TwoLanesSubscribedToTheSameEventEachReceiveOneCopyAndRunOnlyTheirOwnHandlers()
    {
        await StartAsync(host => host
            .AddIntegrationEventLane(InMemoryLane("lane-a"), ErrorQueue, _quickRetry, lane => lane
                .Handle<CrateShipped, LaneAShippedHandler>()
                .Handle<CrateReturned, LaneAReturnedHandler>()
                .Forward<LaneCallLog>())
            .AddIntegrationEventLane(InMemoryLane("lane-b"), ErrorQueue, _quickRetry, lane => lane
                .Handle<CrateShipped, LaneBShippedHandler>()
                .Forward<LaneCallLog>()));

        var shipped = new CrateShipped(Guid.NewGuid());
        var returned = new CrateReturned(Guid.NewGuid());

        await DispatchAsync(shipped);
        await DispatchAsync(returned);
        await CallLog.WaitForAsync(3);

        CallLog.Calls.Select(call => (call.Handler, call.Event)).Should().BeEquivalentTo(new (string, IIntegrationEvent)[]
        {
            (nameof(LaneAShippedHandler), shipped),
            (nameof(LaneBShippedHandler), shipped),
            (nameof(LaneAReturnedHandler), returned)
        });
    }

    [Fact]
    public async Task FailingHandlerIsDeferredByTheBackoffAndThenDeadLettered()
    {
        await StartAsync(host => host.AddIntegrationEventLane(InMemoryLane("lane-a"), ErrorQueue, _quickRetry, lane => lane
            .Handle<CrateShipped, FailingShippedHandler>()
            .Forward<LaneCallLog>()));

        var shipped = new CrateShipped(Guid.NewGuid());

        await DispatchAsync(shipped);
        await CallLog.WaitForAsync(6);
        await WaitForErrorQueueAsync();

        CallLog.Calls.Select(call => call.DeferCount).Should().Equal("0", "0", "1", "1", "2", "2");
        CallLog.Calls.Should().AllSatisfy(call => call.MessageId.Should().Be(shipped.Id.ToString()));

        var deadLetter = _network.GetMessages(ErrorQueue).Should().ContainSingle().Subject;
        deadLetter.Headers[Headers.MessageId].Should().Be(shipped.Id.ToString());
        deadLetter.Headers[Headers.ErrorDetails].Should()
            .Contain("Gave up after 2 deferrals")
            .And.Contain("Courier API unavailable");
    }

    [Fact]
    public async Task LaneWhoseHandlerDependsOnATypeThatIsNotForwardedFailsHostStartup()
    {
        var start = () => StartAsync(host => host.AddIntegrationEventLane(InMemoryLane("lane-a"), ErrorQueue, _quickRetry, lane => lane
            .Handle<CrateShipped, LaneAShippedHandler>()
            .Handle<CrateReturned, LabelPrintingReturnedHandler>()
            .Forward<LaneCallLog>()));

        var failure = await start.Should().ThrowAsync<InvalidOperationException>();

        failure.WithMessage($"*lane-a*{typeof(CrateReturned).FullName}*{nameof(LabelPrinter)}*");
        failure.Which.InnerException.Should().NotBeNull();
        CallLog.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task LaneWithEveryHandlerDependencyForwardedStartsAndHandlesMessages()
    {
        await StartAsync(host => host.AddIntegrationEventLane(InMemoryLane("lane-a"), ErrorQueue, _quickRetry, lane => lane
            .Handle<CrateShipped, LaneAShippedHandler>()
            .Handle<CrateReturned, LabelPrintingReturnedHandler>()
            .Forward<LaneCallLog>()
            .Forward<LabelPrinter>()));

        var returned = new CrateReturned(Guid.NewGuid());

        await DispatchAsync(returned);
        await CallLog.WaitForAsync(1);

        CallLog.Calls.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Handler = $"RETURN-{returned.CrateId}",
            Event = (IIntegrationEvent)returned
        });
    }

    private async Task WaitForErrorQueueAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        while (_network.GetCount(ErrorQueue) == 0)
        {
            await Task.Delay(50, timeout.Token);
        }
    }
}

public class IntegrationEventLaneRegistrationTests
{
    [Fact]
    public void LaneWithoutHandlersIsRejected()
    {
        var retryPolicy = IntegrationEventRetryPolicy
            .Create(3, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30))
            .Unwrap();

        var addLane = () => new HostBuilder().AddIntegrationEventLane(
            rebus => rebus.Transport(transport => transport.UseInMemoryTransport(new InMemNetwork(), "lane-a")),
            "lanes.error",
            retryPolicy,
            lane => lane.Forward<LaneCallLog>());

        addLane.Should().Throw<InvalidOperationException>();
    }
}
