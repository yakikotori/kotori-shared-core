using Kotori.SharedCore.Outbox;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kotori.SharedCore.EntityFrameworkCore.Outbox;

public class BackgroundOutboxProcessor : BackgroundService
{
    private readonly ILogger<BackgroundOutboxProcessor> _logger;
    private readonly IOptions<BackgroundOutboxProcessorOptions> _options;
    private readonly IOutboxProcessor _outboxProcessor;

    public BackgroundOutboxProcessor(
        ILogger<BackgroundOutboxProcessor> logger,
        IOptions<BackgroundOutboxProcessorOptions> options, 
        IOutboxProcessor outboxProcessor)
    {
        _logger = logger;
        _options = options;
        _outboxProcessor = outboxProcessor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _outboxProcessor.ProcessAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in BackgroundOutboxProcessor");
            }
            finally
            {
                await Task.Delay(_options.Value.DelayMs, stoppingToken);
            }
        }
    }
}