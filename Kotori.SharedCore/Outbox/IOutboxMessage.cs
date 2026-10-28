namespace Kotori.SharedCore.Outbox;

public interface IOutboxMessage : IAggregateRoot
{
    string Type { get; }
    string Payload { get; }
    DateTime OccurredOnUtc { get; }
    DateTime? ProcessedOnUtc { get; }
    OutboxMessageState State { get; }
    string? ErrorMessage { get; }
    int Attempts { get; }
    DateTime NextAttemptOnUtc { get; }

    void MarkAsProcessed(DateTime processedOnUtc);

    void MarkAsFailed(DateTime failedOnUtc, string errorMessage);

    void ScheduleRetry(DateTime nextAttemptOnUtc, string errorMessage);
}