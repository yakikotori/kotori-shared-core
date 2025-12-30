namespace Kotori.SharedCore.EntityFrameworkCore.Outbox;

public record OutboxCleanerOptions
{
    public const string SectionName = "OutboxCleaner";
    
    public required TimeSpan ClearProcessedAfter { get; init; }
    public required TimeSpan ClearFailedAfter { get; init; }
}