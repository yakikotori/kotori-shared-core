using Kotori.SharedCore.Inbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kotori.SharedCore.EntityFrameworkCore.Inbox;

public class InboxCleaner : IInboxCleaner
{
    private readonly ILogger<InboxCleaner> _logger;
    private readonly IOptions<InboxCleanerOptions> _options;
    private readonly IUnitOfWorkFactory _uowFactory;

    public InboxCleaner(
        ILogger<InboxCleaner> logger,
        IOptions<InboxCleanerOptions> options,
        IUnitOfWorkFactory uowFactory)
    {
        _logger = logger;
        _options = options;
        _uowFactory = uowFactory;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var uow = await _uowFactory.CreateAsync();

        var inboxMessageRepository = uow.GetRepository<IInboxMessageRepository>();

        var clearedCount = await inboxMessageRepository.RemoveProcessedAsync(_options.Value.ClearProcessedAfter);

        await uow.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Cleared inbox messages Count: {Count}", clearedCount);
    }
}
