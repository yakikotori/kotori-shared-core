using Kotori.SharedCore.Inbox;
using Microsoft.EntityFrameworkCore;

namespace Kotori.SharedCore.EntityFrameworkCore.Inbox;

public class EfInboxMessageRepository : IInboxMessageRepository
{
    private readonly DbContext _context;
    private readonly TimeProvider _timeProvider;

    public EfInboxMessageRepository(DbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public Task<bool> ContainsAsync(Guid messageId, string consumer)
        => _context.Set<InboxMessageEntity>()
            .AnyAsync(message => message.MessageId == messageId && message.Consumer == consumer);

    public Task AddAsync(Guid messageId, string consumer, DateTime processedOnUtc)
        => _context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO inbox_messages ("MessageId", "Consumer", "ProcessedOnUtc")
            VALUES ({messageId}, {consumer}, {processedOnUtc})
            ON CONFLICT DO NOTHING
            """);

    public async Task<int> RemoveProcessedAsync(TimeSpan timePassed)
    {
        var threshold = _timeProvider.GetUtcNow().UtcDateTime - timePassed;

        return await _context.Set<InboxMessageEntity>()
            .Where(message => message.ProcessedOnUtc < threshold)
            .ExecuteDeleteAsync();
    }
}
