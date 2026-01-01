using Kotori.SharedCore.Outbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kotori.SharedCore.EntityFrameworkCore.Outbox;

public class OutboxCleaner : IOutboxCleaner
{
    private readonly ILogger<OutboxCleaner> _logger;
    private readonly IOptions<OutboxCleanerOptions> _options;
    private readonly IUnitOfWorkFactory _uowFactory;

    public OutboxCleaner(
        ILogger<OutboxCleaner> logger,
        IOptions<OutboxCleanerOptions> options,
        IUnitOfWorkFactory uowFactory)
    {
        _logger = logger;
        _options = options;
        _uowFactory = uowFactory;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var uow = await _uowFactory.CreateAsync();
        
        var outboxMessageRepository = uow.GetRepository<IOutboxMessageRepository>();

        var clearedCount = 0;
        
        clearedCount += await outboxMessageRepository.RemoveProcessedAsync(_options.Value.ClearProcessedAfter);
        clearedCount += await outboxMessageRepository.RemoveFailedAsync(_options.Value.ClearFailedAfter);

        await uow.SaveChangesAsync(cancellationToken);
        
        _logger.LogInformation("Cleared domain outbox messages Count: {Count}", clearedCount);
    }
}