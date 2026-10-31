using Kotori.SharedCore.Inbox;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kotori.SharedCore.EntityFrameworkCore.Inbox;

public class BackgroundInboxCleaner : BackgroundService
{
    private readonly ILogger<BackgroundInboxCleaner> _logger;
    private readonly IOptions<BackgroundInboxCleanerOptions> _options;
    private readonly IInboxCleaner _inboxCleaner;

    public BackgroundInboxCleaner(
        ILogger<BackgroundInboxCleaner> logger,
        IOptions<BackgroundInboxCleanerOptions> options,
        IInboxCleaner inboxCleaner)
    {
        _logger = logger;
        _options = options;
        _inboxCleaner = inboxCleaner;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _inboxCleaner.ClearAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in BackgroundInboxCleaner");
            }
            finally
            {
                await Task.Delay(_options.Value.DelayMs, stoppingToken);
            }
        }
    }
}
