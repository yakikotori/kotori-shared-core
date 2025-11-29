using Kotori.SharedCore.Outbox;

namespace Kotori.SharedCore.EntityFrameworkCore.Outbox;

public readonly record struct DomainOutboxMessageEntityId(Guid Value);

public class OutboxMessageEntity : EntityBase, IOutboxMessage
{
    public DomainOutboxMessageEntityId Id { get; private init; }

    public string Type { get; private init; } = null!;
    public string Payload { get; private init; } = null!;
    public DateTime OccurredOnUtc { get; private init; }
    public DateTime? ProcessedOnUtc { get; private set; }
    public OutboxMessageState State { get; private set; }
    public string? ErrorMessage { get; private set; } = null!;

    private OutboxMessageEntity()
    {
    }

    public static OutboxMessageEntity Create(string type, string payload, DateTime occurredOnUtc)
        => new()
        {
            Id = new DomainOutboxMessageEntityId(Guid.CreateVersion7()),
            Type = type,
            Payload = payload,
            OccurredOnUtc = occurredOnUtc,
            State = OutboxMessageState.Pending
        };
    
    public void MarkAsProcessed(DateTime processedOnUtc)
    {
        ProcessedOnUtc = processedOnUtc;
        State = OutboxMessageState.Processed;
    }
    
    public void MarkAsFailed(DateTime failedOnUtc, string errorMessage)
    {
        ProcessedOnUtc = failedOnUtc;
        State = OutboxMessageState.Failed;
        ErrorMessage = errorMessage;
    }
}