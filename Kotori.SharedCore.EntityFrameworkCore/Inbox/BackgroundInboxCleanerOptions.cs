namespace Kotori.SharedCore.EntityFrameworkCore.Inbox;

public record BackgroundInboxCleanerOptions
{
    public const string SectionName = "BackgroundInboxCleaner";

    public required int DelayMs { get; init; } = 60 * 1000;
}
