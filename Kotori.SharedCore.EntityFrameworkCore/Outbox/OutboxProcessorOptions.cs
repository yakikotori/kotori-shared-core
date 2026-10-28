namespace Kotori.SharedCore.EntityFrameworkCore.Outbox;

public record OutboxProcessorOptions
{
    public const string SectionName = "OutboxProcessor";
    
    public required int BatchSize { get; init; }
    public int MaxAttempts { get; init; } = 10;
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan RetryMaxDelay { get; init; } = TimeSpan.FromMinutes(10);
}