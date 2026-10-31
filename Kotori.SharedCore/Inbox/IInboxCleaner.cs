namespace Kotori.SharedCore.Inbox;

public interface IInboxCleaner
{
    Task ClearAsync(CancellationToken cancellationToken = default);
}
