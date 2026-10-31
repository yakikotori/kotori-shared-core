using Kotori.SharedCore.Inbox;

namespace Kotori.SharedCore.Rebus;

internal sealed class NoInbox : IInbox
{
    public Task<bool> HasProcessedAsync(Guid messageId, string consumer, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task MarkProcessedAsync(Guid messageId, string consumer, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
