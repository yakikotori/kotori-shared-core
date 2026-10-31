using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Kotori.SharedCore.Rebus;

public sealed class IntegrationEventLaneMetrics : IIntegrationEventLaneMetrics
{
    private static readonly double[] HandlerDurationBuckets = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60];

    private readonly Counter<long> _handled;
    private readonly Histogram<double> _handlerDuration;
    private readonly Counter<long> _deferred;
    private readonly Counter<long> _deadLettered;
    private readonly Counter<long> _duplicatesSkipped;

    public IntegrationEventLaneMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(KotoriSharedCoreMetrics.MeterName);

        _handled = meter.CreateCounter<long>(
            "kotori.lane.messages.handled",
            "{message}",
            "Messages whose handlers all succeeded in a lane");
        _handlerDuration = meter.CreateHistogram(
            "kotori.lane.handler.duration",
            "s",
            "Time a lane spent running every handler of a message that succeeded",
            [],
            new InstrumentAdvice<double> { HistogramBucketBoundaries = HandlerDurationBuckets });
        _deferred = meter.CreateCounter<long>(
            "kotori.lane.messages.deferred",
            "{message}",
            "Messages a lane deferred after their immediate attempts failed");
        _deadLettered = meter.CreateCounter<long>(
            "kotori.lane.messages.dead_lettered",
            "{message}",
            "Messages a lane moved to its error queue after its retry policy gave up");
        _duplicatesSkipped = meter.CreateCounter<long>(
            "kotori.inbox.duplicates.skipped",
            "{message}",
            "Messages a lane skipped because its inbox had already recorded them");
    }

    public void RecordHandled(IntegrationEventLaneConsumer lane, Type eventType, TimeSpan duration)
    {
        var tags = TagsOf(lane, eventType);

        _handled.Add(1, tags);
        _handlerDuration.Record(duration.TotalSeconds, tags);
    }

    public void RecordDuplicateSkipped(IntegrationEventLaneConsumer lane, Type eventType)
        => _duplicatesSkipped.Add(1, TagsOf(lane, eventType));

    public void RecordDeferred(IntegrationEventLaneConsumer lane, Type eventType, TimeSpan delay)
        => _deferred.Add(1, TagsOf(lane, eventType));

    public void RecordDeadLettered(IntegrationEventLaneConsumer lane, Type eventType)
        => _deadLettered.Add(1, TagsOf(lane, eventType));

    private static TagList TagsOf(IntegrationEventLaneConsumer lane, Type eventType)
        => new()
        {
            { "event.type", eventType.Name },
            { "lane", lane.Name }
        };
}
