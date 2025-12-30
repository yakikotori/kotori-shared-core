namespace Kotori.SharedCore.Outbox;

public interface IOutboxProcessor
{
    Task ProcessAsync(CancellationToken cancellationToken = default);
}