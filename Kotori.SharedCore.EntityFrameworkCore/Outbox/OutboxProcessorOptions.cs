namespace Kotori.SharedCore.EntityFrameworkCore.Outbox;

public record OutboxProcessorOptions
{
    public const string SectionName = "OutboxProcessor";
    
    public required int BatchSize { get; init; }
}