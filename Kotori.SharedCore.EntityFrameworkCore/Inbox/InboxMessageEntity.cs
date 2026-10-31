using Kotori.SharedCore.Inbox;

namespace Kotori.SharedCore.EntityFrameworkCore.Inbox;

public class InboxMessageEntity : EntityBase, IInboxMessage
{
    public const int MaxConsumerLength = 200;

    public Guid MessageId { get; private init; }
    public string Consumer { get; private init; } = null!;
    public DateTime ProcessedOnUtc { get; private init; }

    private InboxMessageEntity()
    {
    }

    public static InboxMessageEntity Create(Guid messageId, string consumer, DateTime processedOnUtc)
        => new()
        {
            MessageId = messageId,
            Consumer = consumer,
            ProcessedOnUtc = processedOnUtc
        };
}
