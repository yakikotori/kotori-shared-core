using Kotori.SharedCore.DomainEvents;
using Kotori.SharedCore.Outbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kotori.SharedCore.EntityFrameworkCore.Outbox;

public class OutboxProcessor : IOutboxProcessor
{
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly IUnitOfWorkFactory _uowFactory;
    private readonly IEventSerializer _eventSerializer;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<OutboxProcessorOptions> _options;

    public OutboxProcessor(
        ILogger<OutboxProcessor> logger,
        IUnitOfWorkFactory uowFactory, 
        IEventSerializer eventSerializer, 
        IDomainEventDispatcher domainEventDispatcher,
        TimeProvider timeProvider,
        IOptions<OutboxProcessorOptions> options)
    {
        _logger = logger;
        _uowFactory = uowFactory;
        _eventSerializer = eventSerializer;
        _domainEventDispatcher = domainEventDispatcher;
        _timeProvider = timeProvider;
        _options = options;
    }

    public async Task ProcessAsync()
    {
        await using var uow = await _uowFactory.CreateAsync();
        
        var domainOutboxMessageRepository = uow.GetRepository<IOutboxMessageRepository>();

        var domainOutboxMessages = await domainOutboxMessageRepository.GetPendingAsync(_options.Value.BatchSize);
        
        foreach (var domainOutboxMessage in domainOutboxMessages)
        {
            /*
            var eventType = Type.GetType(domainOutboxMessage.Type);

            if (eventType is null)
            {
                _logger.LogWarning(
                    "Failed to process outbox message Error: {ErrorMessage}", 
                    $"Event type {domainOutboxMessage.Type} not found");
                domainOutboxMessage.MarkAsFailed(_timeProvider.GetUtcNow().DateTime, "Event type not found");
                continue;
            }
            
            var deserializeDomainEventResult = _eventSerializer.Deserialize(
                eventType,
                domainOutboxMessage.Payload);

            if (deserializeDomainEventResult is Fail<IDomainEvent, TextError> deserializeDomainEventFail)
            {
                _logger.LogWarning(
                    "Failed to process outbox message Error: {ErrorMessage}", 
                    deserializeDomainEventFail.Error.Message);
                domainOutboxMessage.MarkAsFailed(_timeProvider.GetUtcNow().DateTime, deserializeDomainEventFail.Error.Message);
                continue;
            }

            var domainEvent = deserializeDomainEventResult.Unwrap();

            try
            {
                await _domainEventDispatcher.DispatchAsync(domainEvent);

                domainOutboxMessage.MarkAsProcessed(_timeProvider.GetUtcNow().DateTime);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Exception while dispatching domain event");
                domainOutboxMessage.MarkAsFailed(_timeProvider.GetUtcNow().DateTime, e.Message);
            }
            */
        }

        await uow.SaveChangesAsync();
    }
}