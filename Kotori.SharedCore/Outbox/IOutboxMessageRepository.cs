using Kotori.SharedCore.IntegrationEvents;

namespace Kotori.SharedCore.Outbox;

public interface IOutboxMessageRepository : IRepository<IOutboxMessage>
{
    void Add(IIntegrationEvent integrationEvent, DateTime occurredOnUtc);
    
    Task<List<IOutboxMessage>> GetPendingAsync(int limit);
    
    Task<int> RemoveProcessedAsync(TimeSpan timePassed);
    
    Task<int> RemoveFailedAsync(TimeSpan timePassed);
}