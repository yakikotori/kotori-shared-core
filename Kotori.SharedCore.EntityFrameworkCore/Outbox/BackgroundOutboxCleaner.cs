using Kotori.SharedCore.Outbox;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kotori.SharedCore.EntityFrameworkCore.Outbox;

public class BackgroundOutboxCleaner : BackgroundService
{
    private readonly ILogger<BackgroundOutboxCleaner> _logger;
    private readonly IOptions<BackgroundOutboxCleanerOptions> _options;
    private readonly IOutboxCleaner _outboxCleaner;

    public BackgroundOutboxCleaner(
        ILogger<BackgroundOutboxCleaner> logger,
        IOptions<BackgroundOutboxCleanerOptions> options,
        IOutboxCleaner outboxCleaner)
    {
        _logger = logger;
        _options = options;
        _outboxCleaner = outboxCleaner;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _outboxCleaner.ClearAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in BackgroundOutboxCleaner");
            }
            finally
            {
                await Task.Delay(_options.Value.DelayMs, stoppingToken);
            }
        }
    }
}