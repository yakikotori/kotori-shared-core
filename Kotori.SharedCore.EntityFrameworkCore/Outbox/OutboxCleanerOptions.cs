namespace Kotori.SharedCore.EntityFrameworkCore.Outbox;

public class OutboxCleanerOptions
{
    public TimeSpan ClearProcessedAfter { get; set; }
    public TimeSpan ClearFailedAfter { get; set; }
}