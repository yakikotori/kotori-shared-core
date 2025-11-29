using Kotori.SharedCore.Outbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kotori.SharedCore.EntityFrameworkCore.Outbox;

public class OutboxCleaner : IOutboxCleaner
{
    private readonly ILogger<OutboxCleaner> _logger;
    private readonly IUnitOfWorkFactory _uowFactory;
    private readonly IOptions<OutboxCleanerOptions> _options;

    public OutboxCleaner(
        ILogger<OutboxCleaner> logger, 
        IUnitOfWorkFactory uowFactory,
        IOptions<OutboxCleanerOptions> options)
    {
        _logger = logger;
        _uowFactory = uowFactory;
        _options = options;
    }

    public async Task ClearAsync()
    {
        await using var uow = await _uowFactory.CreateAsync();
        
        var domainOutboxMessageRepository = uow.GetRepository<IOutboxMessageRepository>();

        var clearedCount = 0;
        
        clearedCount += await domainOutboxMessageRepository.RemoveProcessedAsync(_options.Value.ClearProcessedAfter);
        clearedCount += await domainOutboxMessageRepository.RemoveFailedAsync(_options.Value.ClearFailedAfter);

        await uow.SaveChangesAsync();
        
        _logger.LogInformation("Cleared domain outbox messages Count: {Count}", clearedCount);
    }
}