namespace Kotori.SharedCore.Inbox;

public interface IInbox
{
    Task<bool> HasProcessedAsync(Guid messageId, string consumer, CancellationToken cancellationToken = default);

    Task MarkProcessedAsync(Guid messageId, string consumer, CancellationToken cancellationToken = default);
}
