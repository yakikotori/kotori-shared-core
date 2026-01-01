namespace Kotori.SharedCore.EntityFrameworkCore.Outbox;

public record BackgroundOutboxCleanerOptions
{
    public const string SectionName = "BackgroundOutboxCleaner";
    
    public required int DelayMs { get; init; }
}