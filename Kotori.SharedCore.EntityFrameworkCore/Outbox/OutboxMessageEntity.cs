using Kotori.SharedCore.Outbox;

namespace Kotori.SharedCore.EntityFrameworkCore.Outbox;

public readonly record struct OutboxMessageEntityId(Guid Value);

public class OutboxMessageEntity : EntityBase, IOutboxMessage
{
    public const int MaxErrorMessageLength = 3000;

    public OutboxMessageEntityId Id { get; private init; }

    public string Type { get; private init; } = null!;
    public string Payload { get; private init; } = null!;
    public DateTime OccurredOnUtc { get; private init; }
    public DateTime? ProcessedOnUtc { get; private set; }
    public OutboxMessageState State { get; private set; }
    public string? ErrorMessage { get; private set; } = null!;
    public int Attempts { get; private set; }
    public DateTime NextAttemptOnUtc { get; private set; }

    private OutboxMessageEntity()
    {
    }

    public static OutboxMessageEntity Create(string type, string payload, DateTime occurredOnUtc)
        => new()
        {
            Id = new OutboxMessageEntityId(Guid.CreateVersion7()),
            Type = type,
            Payload = payload,
            OccurredOnUtc = occurredOnUtc,
            State = OutboxMessageState.Pending,
            NextAttemptOnUtc = occurredOnUtc
        };

    public void MarkAsProcessed(DateTime processedOnUtc)
    {
        Attempts++;
        ProcessedOnUtc = processedOnUtc;
        State = OutboxMessageState.Processed;
    }

    public void MarkAsFailed(DateTime failedOnUtc, string errorMessage)
    {
        Attempts++;
        ProcessedOnUtc = failedOnUtc;
        State = OutboxMessageState.Failed;
        ErrorMessage = Truncate(errorMessage);
    }

    public void ScheduleRetry(DateTime nextAttemptOnUtc, string errorMessage)
    {
        Attempts++;
        NextAttemptOnUtc = nextAttemptOnUtc;
        ErrorMessage = Truncate(errorMessage);
    }

    private static string Truncate(string errorMessage)
        => errorMessage.Length > MaxErrorMessageLength
            ? errorMessage[..MaxErrorMessageLength]
            : errorMessage;
}