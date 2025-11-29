namespace Kotori.SharedCore.Outbox;

public interface IOutboxProcessor
{
    Task ProcessAsync();
}