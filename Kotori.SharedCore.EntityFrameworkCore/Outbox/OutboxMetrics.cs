using System.Diagnostics.Metrics;
using Kotori.SharedCore.Outbox;

namespace Kotori.SharedCore.EntityFrameworkCore.Outbox;

public sealed class OutboxMetrics : IOutboxMetrics
{
    private static readonly double[] RelayLagBuckets = [0.01, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 300, 900, 3600];

    private readonly Histogram<double> _relayLag;
    private readonly Counter<long> _publishFailures;
    private readonly Counter<long> _gaveUp;

    public OutboxMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(KotoriSharedCoreMetrics.MeterName);

        _relayLag = meter.CreateHistogram(
            "kotori.outbox.relay.lag",
            "s",
            "Time from an outbox message occurring to its successful dispatch",
            [],
            new InstrumentAdvice<double> { HistogramBucketBoundaries = RelayLagBuckets });
        _publishFailures = meter.CreateCounter<long>(
            "kotori.outbox.publish.failures",
            "{attempt}",
            "Outbox dispatch attempts that threw");
        _gaveUp = meter.CreateCounter<long>(
            "kotori.outbox.messages.gave_up",
            "{message}",
            "Outbox messages marked failed after their last attempt");
    }

    public void RecordPublished(Type eventType, TimeSpan lag)
        => _relayLag.Record(lag.TotalSeconds, EventTypeTag(eventType));

    public void RecordPublishFailed(Type eventType)
        => _publishFailures.Add(1, EventTypeTag(eventType));

    public void RecordGaveUp(Type eventType)
        => _gaveUp.Add(1, EventTypeTag(eventType));

    private static KeyValuePair<string, object?> EventTypeTag(Type eventType)
        => new("event.type", eventType.Name);
}
