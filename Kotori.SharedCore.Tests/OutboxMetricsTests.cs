using System.Diagnostics.Metrics;
using FluentAssertions;
using Kotori.SharedCore.EntityFrameworkCore.Outbox;
using Kotori.SharedCore.IntegrationEvents;
using Kotori.SharedCore.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Kotori.SharedCore.Tests;

public class OutboxMetricsTests : IDisposable
{
    private const string RelayLag = "kotori.outbox.relay.lag";
    private const string PublishFailures = "kotori.outbox.publish.failures";
    private const string GaveUp = "kotori.outbox.messages.gave_up";

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _metricsProvider = new ServiceCollection().AddMetrics().BuildServiceProvider();
    private readonly MetricCollector<double> _relayLag;
    private readonly MetricCollector<long> _publishFailures;
    private readonly MetricCollector<long> _gaveUp;

    public OutboxMetricsTests()
    {
        var meterFactory = _metricsProvider.GetRequiredService<IMeterFactory>();

        _relayLag = new MetricCollector<double>(meterFactory, KotoriSharedCoreMetrics.MeterName, RelayLag);
        _publishFailures = new MetricCollector<long>(meterFactory, KotoriSharedCoreMetrics.MeterName, PublishFailures);
        _gaveUp = new MetricCollector<long>(meterFactory, KotoriSharedCoreMetrics.MeterName, GaveUp);
    }

    public void Dispose()
    {
        _relayLag.Dispose();
        _publishFailures.Dispose();
        _gaveUp.Dispose();
        _metricsProvider.Dispose();
    }

    private OutboxMetrics CreateMetrics() => new(_metricsProvider.GetRequiredService<IMeterFactory>());

    [Fact]
    public void RecordPublished_RecordsTheLagInSecondsTaggedWithTheShortEventTypeName()
    {
        CreateMetrics().RecordPublished(typeof(OutboxTestEvent), TimeSpan.FromMilliseconds(1500));

        var measurement = _relayLag.GetMeasurementSnapshot().Should().ContainSingle().Subject;
        measurement.Value.Should().Be(1.5);
        measurement.Tags.Should().Equal(new Dictionary<string, object?> { ["event.type"] = nameof(OutboxTestEvent) });
        _relayLag.Instrument.Should().BeOfType<Histogram<double>>().Which.Unit.Should().Be("s");
    }

    [Fact]
    public void RecordPublishFailed_CountsOneFailureTaggedWithTheEventType()
    {
        CreateMetrics().RecordPublishFailed(typeof(OutboxTestEvent));

        var measurement = _publishFailures.GetMeasurementSnapshot().Should().ContainSingle().Subject;
        measurement.Value.Should().Be(1);
        measurement.Tags.Should().Equal(new Dictionary<string, object?> { ["event.type"] = nameof(OutboxTestEvent) });
        _publishFailures.Instrument.Should().BeOfType<Counter<long>>();
    }

    [Fact]
    public void RecordGaveUp_CountsOneMessageTaggedWithTheEventType()
    {
        CreateMetrics().RecordGaveUp(typeof(OutboxTestEvent));

        var measurement = _gaveUp.GetMeasurementSnapshot().Should().ContainSingle().Subject;
        measurement.Value.Should().Be(1);
        measurement.Tags.Should().Equal(new Dictionary<string, object?> { ["event.type"] = nameof(OutboxTestEvent) });
        _gaveUp.Instrument.Should().BeOfType<Counter<long>>();
    }

    [Fact]
    public async Task ProcessAsync_RecordsTheRelayLag_WhenDispatchSucceeds()
    {
        var message = OutboxMessageEntity.Create("OutboxTestEvent", "{}", Now.UtcDateTime.AddSeconds(-90));
        var batch = CreateBatch(message, maxAttempts: 10);

        await batch.Processor.ProcessAsync();

        message.State.Should().Be(OutboxMessageState.Processed);
        var measurement = _relayLag.GetMeasurementSnapshot().Should().ContainSingle().Subject;
        measurement.Value.Should().Be(90);
        measurement.Tags["event.type"].Should().Be(nameof(OutboxTestEvent));
        _publishFailures.GetMeasurementSnapshot().Should().BeEmpty();
        _gaveUp.GetMeasurementSnapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_RecordsAPublishFailureButNotGivingUp_WhenDispatchThrowsBeforeTheLastAttempt()
    {
        var message = OutboxMessageEntity.Create("OutboxTestEvent", "{}", Now.UtcDateTime);
        var batch = CreateBatch(message, maxAttempts: 3);

        batch.Dispatcher.DispatchAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("Broker unreachable")));

        await batch.Processor.ProcessAsync();

        message.State.Should().Be(OutboxMessageState.Pending);
        _publishFailures.GetMeasurementSnapshot().Should().ContainSingle()
            .Which.Tags["event.type"].Should().Be(nameof(OutboxTestEvent));
        _gaveUp.GetMeasurementSnapshot().Should().BeEmpty();
        _relayLag.GetMeasurementSnapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_RecordsGivingUp_WhenTheLastAttemptFails()
    {
        var message = OutboxMessageEntity.Create("OutboxTestEvent", "{}", Now.UtcDateTime);
        var batch = CreateBatch(message, maxAttempts: 2);

        batch.Dispatcher.DispatchAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("Broker unreachable")));

        await batch.Processor.ProcessAsync();
        _gaveUp.GetMeasurementSnapshot().Should().BeEmpty();

        await batch.Processor.ProcessAsync();

        message.State.Should().Be(OutboxMessageState.Failed);
        _publishFailures.GetMeasurementSnapshot().Should().HaveCount(2);
        _gaveUp.GetMeasurementSnapshot().Should().ContainSingle()
            .Which.Tags["event.type"].Should().Be(nameof(OutboxTestEvent));
    }

    [Fact]
    public void AddOutbox_RegistersTheMeterBasedMetrics_WithoutAHost()
    {
        var services = new ServiceCollection();

        services.AddOutbox(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOutboxMetrics>().Should().BeOfType<OutboxMetrics>();
    }

    [Fact]
    public void AddOutbox_KeepsOutboxMetricsTheServiceRegistered()
    {
        var metrics = Substitute.For<IOutboxMetrics>();
        var services = new ServiceCollection();

        services.AddSingleton(metrics);
        services.AddOutbox(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOutboxMetrics>().Should().BeSameAs(metrics);
    }

    private OutboxBatch CreateBatch(OutboxMessageEntity message, int maxAttempts)
    {
        var options = Substitute.For<IOptions<OutboxProcessorOptions>>();
        var uowFactory = Substitute.For<IUnitOfWorkFactory>();
        var uow = Substitute.For<IUnitOfWork>();
        var repository = Substitute.For<IOutboxMessageRepository>();
        var eventSerializer = Substitute.For<IEventSerializer>();
        var dispatcher = Substitute.For<IIntegrationEventDispatcher>();
        var timeProvider = Substitute.For<TimeProvider>();

        options.Value.Returns(new OutboxProcessorOptions
        {
            BatchSize = 1,
            MaxAttempts = maxAttempts,
            RetryBaseDelay = TimeSpan.Zero,
            RetryMaxDelay = TimeSpan.Zero
        });
        uowFactory.CreateAsync().Returns(uow);
        uow.GetRepository<IOutboxMessageRepository>().Returns(repository);
        repository.GetPendingAsync(Arg.Any<int>()).Returns(_ => [message]);
        eventSerializer.Deserialize(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new Ok<IIntegrationEvent, TextError>(new OutboxTestEvent()));
        timeProvider.GetUtcNow().Returns(Now);

        var processor = new OutboxProcessor(
            Substitute.For<ILogger<OutboxProcessor>>(),
            options,
            uowFactory,
            eventSerializer,
            dispatcher,
            timeProvider,
            CreateMetrics());

        return new OutboxBatch(processor, dispatcher);
    }

    private sealed record OutboxBatch(OutboxProcessor Processor, IIntegrationEventDispatcher Dispatcher);
}
