namespace Kotori.SharedCore.Outbox;

public enum OutboxMessageState
{
    Pending,
    Processed,
    Failed
}