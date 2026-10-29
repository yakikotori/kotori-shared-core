namespace Kotori.SharedCore.EntityFrameworkCore.Outbox;

public record OutboxCleanerOptions
{
    public const string SectionName = "OutboxCleaner";
    
    public TimeSpan ClearProcessedAfter { get; init; } = TimeSpan.FromDays(7);
    public TimeSpan ClearFailedAfter { get; init; } = TimeSpan.FromDays(30);
}