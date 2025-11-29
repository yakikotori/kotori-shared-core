namespace Kotori.SharedCore.Outbox;

public interface IOutboxCleaner
{
    Task ClearAsync();
}