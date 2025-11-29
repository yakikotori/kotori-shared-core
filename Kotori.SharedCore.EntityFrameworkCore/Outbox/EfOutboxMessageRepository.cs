using Kotori.SharedCore.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Kotori.SharedCore.EntityFrameworkCore.Outbox;

public class EfOutboxMessageRepository : IOutboxMessageRepository
{
    private readonly DbContext _context;
    private readonly TimeProvider _timeProvider;

    public EfOutboxMessageRepository(DbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<List<IOutboxMessage>> GetPendingAsync(int limit)
    {
        var messageEntities = await _context.Set<OutboxMessageEntity>()
            .Where(message => message.State == OutboxMessageState.Pending)
            .Take(limit)
            .ToListAsync();

        return messageEntities.Cast<IOutboxMessage>().ToList();
    }

    public async Task<int> RemoveProcessedAsync(TimeSpan timePassed)
    {
        var now = _timeProvider.GetUtcNow().DateTime;

        var messageEntities = await _context.Set<OutboxMessageEntity>()
            .Where(message =>
                message.State == OutboxMessageState.Processed &&
                now - message.ProcessedOnUtc > timePassed)
            .ToListAsync();
        
        _context.Set<OutboxMessageEntity>().RemoveRange(messageEntities);

        return messageEntities.Count;
    }

    public async Task<int> RemoveFailedAsync(TimeSpan timePassed)
    {
        var now = _timeProvider.GetUtcNow().DateTime;

        var messageEntities = await _context.Set<OutboxMessageEntity>()
            .Where(message =>
                message.State == OutboxMessageState.Failed &&
                now - message.ProcessedOnUtc > timePassed)
            .ToListAsync();
        
        _context.Set<OutboxMessageEntity>().RemoveRange(messageEntities);
        
        return messageEntities.Count;
    }
}