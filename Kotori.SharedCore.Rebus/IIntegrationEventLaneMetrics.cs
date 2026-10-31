namespace Kotori.SharedCore.Rebus;

public interface IIntegrationEventLaneMetrics
{
    void RecordHandled(IntegrationEventLaneConsumer lane, Type eventType, TimeSpan duration);

    void RecordDuplicateSkipped(IntegrationEventLaneConsumer lane, Type eventType);

    void RecordDeferred(IntegrationEventLaneConsumer lane, Type eventType, TimeSpan delay);

    void RecordDeadLettered(IntegrationEventLaneConsumer lane, Type eventType);
}
