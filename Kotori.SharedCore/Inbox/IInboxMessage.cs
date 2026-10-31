namespace Kotori.SharedCore.Inbox;

public interface IInboxMessage : IAggregateRoot
{
    Guid MessageId { get; }
    string Consumer { get; }
    DateTime ProcessedOnUtc { get; }
}
