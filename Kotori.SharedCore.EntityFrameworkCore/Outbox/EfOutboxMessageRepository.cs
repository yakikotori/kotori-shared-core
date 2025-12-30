using Kotori.SharedCore.IntegrationEvents;
using Kotori.SharedCore.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Kotori.SharedCore.EntityFrameworkCore.Outbox;

public class EfOutboxMessageRepository : IOutboxMessageRepository
{
    private readonly DbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly IEventSerializer _eventSerializer;

    public EfOutboxMessageRepository(DbContext context, TimeProvider timeProvider, IEventSerializer eventSerializer)
    {
        _context = context;
        _timeProvider = timeProvider;
        _eventSerializer = eventSerializer;
    }

    public void Add(IIntegrationEvent integrationEvent, DateTime occurredOnUtc)
    {
        var payload = _eventSerializer.Serialize(integrationEvent);

        var outboxMessageEntity = OutboxMessageEntity.Create(
            integrationEvent.GetType().AssemblyQualifiedName!,
            payload,
            occurredOnUtc);
        
        _context.Set<OutboxMessageEntity>().Add(outboxMessageEntity);
    }

    public async Task<List<IOutboxMessage>> GetPendingAsync(int limit)
    {
        var messageEntities = await _context.Set<OutboxMessageEntity>()
            .FromSql(
                $"""
                SELECT * FROM outbox_messages
                WHERE "State" = {(int)OutboxMessageState.Pending}
                ORDER BY "OccurredOnUtc"
                LIMIT {limit}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync();

        return messageEntities.Cast<IOutboxMessage>().ToList();
    }

    public async Task<int> RemoveProcessedAsync(TimeSpan timePassed)
    {
        var threshold = _timeProvider.GetUtcNow().DateTime - timePassed;

        return await _context.Set<OutboxMessageEntity>()
            .Where(message => 
                message.State == OutboxMessageState.Processed && 
                message.ProcessedOnUtc < threshold)
            .ExecuteDeleteAsync();
    }

    public async Task<int> RemoveFailedAsync(TimeSpan timePassed)
    {
        var threshold = _timeProvider.GetUtcNow().DateTime - timePassed;

        return await _context.Set<OutboxMessageEntity>()
            .Where(message => 
                message.State == OutboxMessageState.Failed && 
                message.ProcessedOnUtc < threshold)
            .ExecuteDeleteAsync();
    }
}