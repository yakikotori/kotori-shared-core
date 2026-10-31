using Kotori.SharedCore.IntegrationEvents;
using Kotori.SharedCore.Outbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kotori.SharedCore.EntityFrameworkCore.Outbox;

public class OutboxProcessor : IOutboxProcessor
{
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly IOptions<OutboxProcessorOptions> _options;
    private readonly IUnitOfWorkFactory _uowFactory;
    private readonly IEventSerializer _eventSerializer;
    private readonly IIntegrationEventDispatcher _integrationEventDispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly IOutboxMetrics _metrics;

    public OutboxProcessor(
        ILogger<OutboxProcessor> logger, 
        IOptions<OutboxProcessorOptions> options,
        IUnitOfWorkFactory uowFactory,
        IEventSerializer eventSerializer,
        IIntegrationEventDispatcher integrationEventDispatcher,
        TimeProvider timeProvider,
        IOutboxMetrics metrics)
    {
        _logger = logger;
        _options = options;
        _uowFactory = uowFactory;
        _eventSerializer = eventSerializer;
        _integrationEventDispatcher = integrationEventDispatcher;
        _timeProvider = timeProvider;
        _metrics = metrics;
    }

    public async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        await using var uow = await _uowFactory.CreateAsync();

        await uow.BeginTransactionAsync(cancellationToken);
        
        var outboxMessageRepository = uow.GetRepository<IOutboxMessageRepository>();
        
        var outboxMessages = await outboxMessageRepository.GetPendingAsync(_options.Value.BatchSize);
        
        foreach (var outboxMessage in outboxMessages)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var deserializeEventResult = _eventSerializer.Deserialize(
                outboxMessage.Type,
                outboxMessage.Payload);

            if (deserializeEventResult is Fail<IIntegrationEvent, TextError> deserializeEventFail)
            {
                _logger.LogWarning(
                    "Failed to process outbox message Error: {ErrorMessage}", 
                    deserializeEventFail.Error.Message);
                outboxMessage.MarkAsFailed(_timeProvider.GetUtcNow().UtcDateTime, deserializeEventFail.Error.Message);
                continue;
            }

            var deserializedEvent = deserializeEventResult.Unwrap();

            try
            {
                await _integrationEventDispatcher.DispatchAsync(deserializedEvent, cancellationToken);

                var publishedOnUtc = _timeProvider.GetUtcNow().UtcDateTime;

                outboxMessage.MarkAsProcessed(publishedOnUtc);
                _metrics.RecordPublished(deserializedEvent.GetType(), publishedOnUtc - outboxMessage.OccurredOnUtc);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Outbox dispatch was interrupted by shutdown, the message stays pending");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception while dispatching domain event");
                _metrics.RecordPublishFailed(deserializedEvent.GetType());
                HandleDispatchFailure(outboxMessage, deserializedEvent.GetType(), ex.Message);
            }
        }

        await uow.SaveChangesAsync(CancellationToken.None);
    }

    private void HandleDispatchFailure(IOutboxMessage outboxMessage, Type eventType, string errorMessage)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var attempt = outboxMessage.Attempts + 1;

        if (attempt >= _options.Value.MaxAttempts)
        {
            outboxMessage.MarkAsFailed(now, errorMessage);
            _metrics.RecordGaveUp(eventType);
            return;
        }

        outboxMessage.ScheduleRetry(now + GetRetryDelay(attempt), errorMessage);
    }

    private TimeSpan GetRetryDelay(int attempt)
    {
        var delayTicks = _options.Value.RetryBaseDelay.Ticks * Math.Pow(2, attempt - 1);

        return TimeSpan.FromTicks((long)Math.Min(delayTicks, _options.Value.RetryMaxDelay.Ticks));
    }
}