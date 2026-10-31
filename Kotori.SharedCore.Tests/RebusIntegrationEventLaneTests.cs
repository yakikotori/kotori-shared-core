using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using FluentAssertions;
using Kotori.SharedCore.Inbox;
using Kotori.SharedCore.IntegrationEvents;
using Kotori.SharedCore.Rebus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
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

    private MetricCollector<T> Collect<T>(string instrument) where T : struct
        => new(_host.Services.GetRequiredService<IMeterFactory>(), KotoriSharedCoreMetrics.MeterName, instrument);

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
    public async Task LaneThatUsesTheInboxWithoutAnInboxInTheHostFailsHostStartup()
    {
        var start = () => StartAsync(host => host.AddIntegrationEventLane(InMemoryLane("lane-a"), ErrorQueue, _quickRetry, lane => lane
            .Handle<CrateShipped, LaneAShippedHandler>()
            .Forward<LaneCallLog>()
            .UseInbox()));

        var failure = await start.Should().ThrowAsync<InvalidOperationException>();

        failure.WithMessage($"*lane-a*inbox*{nameof(IInbox)}*");
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

    [Fact]
    public async Task LaneRecordsAHandledMessageWithTheTimeItsHandlersTook()
    {
        var clock = new FakeTimeProvider();

        await StartAsync(host => host
            .ConfigureServices((_, services) => services.AddSingleton(clock).AddSingleton<TimeProvider>(clock))
            .AddIntegrationEventLane(InMemoryLane("lane-a"), ErrorQueue, _quickRetry, lane => lane
                .Handle<CrateShipped, ClockAdvancingShippedHandler>()
                .Handle<CrateShipped, LaneAShippedAuditHandler>()
                .Forward<LaneCallLog>()
                .Forward<FakeTimeProvider>()
                .Forward<TimeProvider>()));

        using var handled = Collect<long>("kotori.lane.messages.handled");
        using var duration = Collect<double>("kotori.lane.handler.duration");

        await DispatchAsync(new CrateShipped(Guid.NewGuid()));
        await duration.WaitForMeasurementsAsync(1, TimeSpan.FromSeconds(30));

        var expectedTags = new Dictionary<string, object?> { ["event.type"] = nameof(CrateShipped), ["lane"] = "lane-a" };
        var count = handled.GetMeasurementSnapshot().Should().ContainSingle().Subject;
        count.Value.Should().Be(1);
        count.Tags.Should().BeEquivalentTo(expectedTags);
        var seconds = duration.GetMeasurementSnapshot().Should().ContainSingle().Subject;
        seconds.Value.Should().Be(2);
        seconds.Tags.Should().BeEquivalentTo(expectedTags);
    }

    [Fact]
    public async Task LaneRecordsEachDeferralOfAFailingMessageAndThenItsDeadLetter()
    {
        await StartAsync(host => host.AddIntegrationEventLane(InMemoryLane("lane-a"), ErrorQueue, _quickRetry, lane => lane
            .Handle<CrateShipped, FailingShippedHandler>()
            .Forward<LaneCallLog>()));

        using var handled = Collect<long>("kotori.lane.messages.handled");
        using var deferred = Collect<long>("kotori.lane.messages.deferred");
        using var deadLettered = Collect<long>("kotori.lane.messages.dead_lettered");

        await DispatchAsync(new CrateShipped(Guid.NewGuid()));
        await deadLettered.WaitForMeasurementsAsync(1, TimeSpan.FromSeconds(30));

        var expectedTags = new Dictionary<string, object?> { ["event.type"] = nameof(CrateShipped), ["lane"] = "lane-a" };
        deferred.GetMeasurementSnapshot().Should().HaveCount(2).And.AllSatisfy(measurement =>
        {
            measurement.Value.Should().Be(1);
            measurement.Tags.Should().BeEquivalentTo(expectedTags);
        });
        deadLettered.GetMeasurementSnapshot().Should().ContainSingle().Which.Tags.Should().BeEquivalentTo(expectedTags);
        handled.GetMeasurementSnapshot().Should().BeEmpty();
        CallLog.Calls.Should().HaveCount(6);
    }

    [Fact]
    public async Task LaneRecordsThroughTheLaneMetricsTheHostRegistered()
    {
        var metrics = new RecordingLaneMetrics();

        await StartAsync(host => host
            .ConfigureServices((_, services) => services.AddSingleton<IIntegrationEventLaneMetrics>(metrics))
            .AddIntegrationEventLane(InMemoryLane("lane-a"), ErrorQueue, _quickRetry, lane => lane
                .Handle<CrateShipped, LaneAShippedHandler>()
                .Forward<LaneCallLog>())
            .AddIntegrationEventLane(InMemoryLane("lane-b"), ErrorQueue, _quickRetry, lane => lane
                .Handle<CrateShipped, LaneBShippedHandler>()
                .Forward<LaneCallLog>()));

        await DispatchAsync(new CrateShipped(Guid.NewGuid()));
        await CallLog.WaitForAsync(2);

        _host.Services.GetRequiredService<IIntegrationEventLaneMetrics>().Should().BeSameAs(metrics);
        metrics.Records.Should().BeEquivalentTo(
            $"handled lane-a {nameof(CrateShipped)}",
            $"handled lane-b {nameof(CrateShipped)}");
    }

    [Fact]
    public async Task HostWhoseLaneMetricsCannotBeBuiltFailsStartupBeforeTheLaneConsumes()
    {
        var start = () => StartAsync(host => host
            .ConfigureServices((_, services) => services.AddSingleton<IIntegrationEventLaneMetrics>(
                _ => throw new InvalidOperationException("Meter provider unavailable")))
            .AddIntegrationEventLane(InMemoryLane("lane-a"), ErrorQueue, _quickRetry, lane => lane
                .Handle<CrateShipped, LaneAShippedHandler>()
                .Forward<LaneCallLog>()));

        var failure = await start.Should().ThrowAsync<InvalidOperationException>();

        failure.WithMessage("Meter provider unavailable");
        CallLog.Calls.Should().BeEmpty();
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

public class ClockAdvancingShippedHandler : IIntegrationEventHandler<CrateShipped>
{
    private readonly FakeTimeProvider _clock;
    private readonly LaneCallLog _log;

    public ClockAdvancingShippedHandler(FakeTimeProvider clock, LaneCallLog log)
    {
        _clock = clock;
        _log = log;
    }

    public Task HandleAsync(CrateShipped integrationEvent, CancellationToken cancellationToken)
    {
        _clock.Advance(TimeSpan.FromSeconds(2));
        _log.Add(nameof(ClockAdvancingShippedHandler), integrationEvent, cancellationToken);
        return Task.CompletedTask;
    }
}

public class RecordingLaneMetrics : IIntegrationEventLaneMetrics
{
    private readonly ConcurrentQueue<string> _records = new();

    public IReadOnlyList<string> Records => _records.ToArray();

    public void RecordHandled(IntegrationEventLaneConsumer lane, Type eventType, TimeSpan duration)
        => _records.Enqueue($"handled {lane.Name} {eventType.Name}");

    public void RecordDuplicateSkipped(IntegrationEventLaneConsumer lane, Type eventType)
        => _records.Enqueue($"skipped {lane.Name} {eventType.Name}");

    public void RecordDeferred(IntegrationEventLaneConsumer lane, Type eventType, TimeSpan delay)
        => _records.Enqueue($"deferred {lane.Name} {eventType.Name} {delay}");

    public void RecordDeadLettered(IntegrationEventLaneConsumer lane, Type eventType)
        => _records.Enqueue($"dead-lettered {lane.Name} {eventType.Name}");
}
