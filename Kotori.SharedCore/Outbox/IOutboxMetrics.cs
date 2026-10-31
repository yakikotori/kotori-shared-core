namespace Kotori.SharedCore.Outbox;

public interface IOutboxMetrics
{
    void RecordPublished(Type eventType, TimeSpan lag);

    void RecordPublishFailed(Type eventType);

    void RecordGaveUp(Type eventType);
}
