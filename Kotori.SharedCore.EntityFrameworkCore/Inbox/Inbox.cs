using Kotori.SharedCore.Inbox;

namespace Kotori.SharedCore.EntityFrameworkCore.Inbox;

public class Inbox : IInbox
{
    private readonly IUnitOfWorkFactory _uowFactory;
    private readonly TimeProvider _timeProvider;

    public Inbox(IUnitOfWorkFactory uowFactory, TimeProvider timeProvider)
    {
        _uowFactory = uowFactory;
        _timeProvider = timeProvider;
    }

    public async Task<bool> HasProcessedAsync(Guid messageId, string consumer, CancellationToken cancellationToken = default)
    {
        await using var uow = await _uowFactory.CreateAsync();

        return await uow.GetRepository<IInboxMessageRepository>().ContainsAsync(messageId, consumer);
    }

    public async Task MarkProcessedAsync(Guid messageId, string consumer, CancellationToken cancellationToken = default)
    {
        await using var uow = await _uowFactory.CreateAsync();

        await uow.GetRepository<IInboxMessageRepository>()
            .AddAsync(messageId, consumer, _timeProvider.GetUtcNow().UtcDateTime);

        await uow.SaveChangesAsync(cancellationToken);
    }
}
