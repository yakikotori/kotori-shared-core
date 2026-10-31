namespace Kotori.SharedCore.Inbox;

public interface IInboxMessageRepository : IRepository<IInboxMessage>
{
    Task<bool> ContainsAsync(Guid messageId, string consumer);

    Task AddAsync(Guid messageId, string consumer, DateTime processedOnUtc);

    Task<int> RemoveProcessedAsync(TimeSpan timePassed);
}
