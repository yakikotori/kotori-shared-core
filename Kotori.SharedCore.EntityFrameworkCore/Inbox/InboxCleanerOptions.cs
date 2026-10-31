namespace Kotori.SharedCore.EntityFrameworkCore.Inbox;

public record InboxCleanerOptions
{
    public const string SectionName = "InboxCleaner";

    public TimeSpan ClearProcessedAfter { get; init; } = TimeSpan.FromDays(7);
}
