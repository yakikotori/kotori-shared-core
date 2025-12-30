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

    public OutboxProcessor(
        ILogger<OutboxProcessor> logger, 
        IOptions<OutboxProcessorOptions> options,
        IUnitOfWorkFactory uowFactory,
        IEventSerializer eventSerializer,
        IIntegrationEventDispatcher integrationEventDispatcher,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _options = options;
        _uowFactory = uowFactory;
        _eventSerializer = eventSerializer;
        _integrationEventDispatcher = integrationEventDispatcher;
        _timeProvider = timeProvider;
    }

    public async Task ProcessAsync()
    {
        await using var uow = await _uowFactory.CreateAsync();
        
        var outboxMessageRepository = uow.GetRepository<IOutboxMessageRepository>();

        var outboxMessages = await outboxMessageRepository.GetPendingAsync(_options.Value.BatchSize);
        
        foreach (var outboxMessage in outboxMessages)
        {
            var eventType = Type.GetType(outboxMessage.Type);

            if (eventType is null)
            {
                _logger.LogWarning(
                    "Failed to process outbox message Error: {ErrorMessage}", 
                    $"Event type {outboxMessage.Type} not found");
                outboxMessage.MarkAsFailed(_timeProvider.GetUtcNow().DateTime, "Event type not found");
                continue;
            }
            
            var deserializeEventResult = _eventSerializer.Deserialize(
                eventType,
                outboxMessage.Payload);

            if (deserializeEventResult is Fail<IIntegrationEvent, TextError> deserializeEventFail)
            {
                _logger.LogWarning(
                    "Failed to process outbox message Error: {ErrorMessage}", 
                    deserializeEventFail.Error.Message);
                outboxMessage.MarkAsFailed(_timeProvider.GetUtcNow().DateTime, deserializeEventFail.Error.Message);
                continue;
            }

            var deserializedEvent = deserializeEventResult.Unwrap();

            try
            {
                await _integrationEventDispatcher.DispatchAsync(deserializedEvent);

                outboxMessage.MarkAsProcessed(_timeProvider.GetUtcNow().DateTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception while dispatching domain event");
                outboxMessage.MarkAsFailed(_timeProvider.GetUtcNow().DateTime, ex.Message);
            }
        }

        await uow.SaveChangesAsync();
    }
}