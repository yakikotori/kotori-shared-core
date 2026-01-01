namespace Kotori.SharedCore.EntityFrameworkCore.Outbox;

public record BackgroundOutboxProcessorOptions
{
    public const string SectionName = "BackgroundOutboxProcessor";

    public required int DelayMs { get; init; } = 500;
}