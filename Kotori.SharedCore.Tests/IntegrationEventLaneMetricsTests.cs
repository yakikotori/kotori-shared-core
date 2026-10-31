using System.Diagnostics.Metrics;
using FluentAssertions;
using Kotori.SharedCore.Rebus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace Kotori.SharedCore.Tests;

public class IntegrationEventLaneMetricsTests : IDisposable
{
    private readonly ServiceProvider _metricsProvider = new ServiceCollection().AddMetrics().BuildServiceProvider();
    private readonly IntegrationEventLaneConsumer _lane = new("shop.notifications");
    private readonly IntegrationEventLaneMetrics _metrics;
    private readonly Dictionary<string, object?> _expectedTags = new()
    {
        ["event.type"] = nameof(CrateShipped),
        ["lane"] = "shop.notifications"
    };

    public IntegrationEventLaneMetricsTests()
    {
        _metrics = new IntegrationEventLaneMetrics(MeterFactory);
    }

    private IMeterFactory MeterFactory => _metricsProvider.GetRequiredService<IMeterFactory>();

    public void Dispose() => _metricsProvider.Dispose();

    private MetricCollector<T> Collect<T>(string instrument) where T : struct
        => new(MeterFactory, KotoriSharedCoreMetrics.MeterName, instrument);

    [Fact]
    public void RecordHandled_CountsTheMessageAndRecordsTheDurationInSeconds()
    {
        using var handled = Collect<long>("kotori.lane.messages.handled");
        using var duration = Collect<double>("kotori.lane.handler.duration");

        _metrics.RecordHandled(_lane, typeof(CrateShipped), TimeSpan.FromMilliseconds(250));

        var count = handled.GetMeasurementSnapshot().Should().ContainSingle().Subject;
        count.Value.Should().Be(1);
        count.Tags.Should().BeEquivalentTo(_expectedTags);
        handled.Instrument.Should().BeOfType<Counter<long>>();

        var seconds = duration.GetMeasurementSnapshot().Should().ContainSingle().Subject;
        seconds.Value.Should().Be(0.25);
        seconds.Tags.Should().BeEquivalentTo(_expectedTags);
        duration.Instrument.Should().BeOfType<Histogram<double>>().Which.Unit.Should().Be("s");
    }

    [Fact]
    public void RecordDuplicateSkipped_CountsOneSkippedMessage()
    {
        using var skipped = Collect<long>("kotori.inbox.duplicates.skipped");

        _metrics.RecordDuplicateSkipped(_lane, typeof(CrateShipped));

        var measurement = skipped.GetMeasurementSnapshot().Should().ContainSingle().Subject;
        measurement.Value.Should().Be(1);
        measurement.Tags.Should().BeEquivalentTo(_expectedTags);
        skipped.Instrument.Should().BeOfType<Counter<long>>();
    }

    [Fact]
    public void RecordDeferred_CountsOneDeferredMessage()
    {
        using var deferred = Collect<long>("kotori.lane.messages.deferred");

        _metrics.RecordDeferred(_lane, typeof(CrateShipped), TimeSpan.FromSeconds(30));

        var measurement = deferred.GetMeasurementSnapshot().Should().ContainSingle().Subject;
        measurement.Value.Should().Be(1);
        measurement.Tags.Should().BeEquivalentTo(_expectedTags);
        deferred.Instrument.Should().BeOfType<Counter<long>>();
    }

    [Fact]
    public void RecordDeadLettered_CountsOneDeadLetteredMessage()
    {
        using var deadLettered = Collect<long>("kotori.lane.messages.dead_lettered");

        _metrics.RecordDeadLettered(_lane, typeof(CrateShipped));

        var measurement = deadLettered.GetMeasurementSnapshot().Should().ContainSingle().Subject;
        measurement.Value.Should().Be(1);
        measurement.Tags.Should().BeEquivalentTo(_expectedTags);
        deadLettered.Instrument.Should().BeOfType<Counter<long>>();
    }
}
