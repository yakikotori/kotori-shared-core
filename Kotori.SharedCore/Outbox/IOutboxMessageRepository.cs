namespace Kotori.SharedCore.Outbox;

public interface IOutboxMessageRepository : IRepository<IOutboxMessage>
{
    Task<List<IOutboxMessage>> GetPendingAsync(int limit);
    
    Task<int> RemoveProcessedAsync(TimeSpan timePassed);
    
    Task<int> RemoveFailedAsync(TimeSpan timePassed);
}