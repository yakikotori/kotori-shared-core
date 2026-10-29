namespace Kotori.SharedCore.Rebus;

public sealed record IntegrationEventRetryPolicy : IValueObject
{
    public int ImmediateAttempts { get; private init; }
    public TimeSpan BaseDelay { get; private init; }
    public TimeSpan MaxDelay { get; private init; }
    public TimeSpan GiveUpAfter { get; private init; }

    private IntegrationEventRetryPolicy()
    {
    }

    public static Result<IntegrationEventRetryPolicy, TextError> Create(
        int immediateAttempts,
        TimeSpan baseDelay,
        TimeSpan maxDelay,
        TimeSpan giveUpAfter)
        => immediateAttempts < 1
            ? new TextError($"Immediate attempts must be at least 1: {immediateAttempts}")
            : baseDelay <= TimeSpan.Zero
                ? new TextError($"Base delay must be positive: {baseDelay}")
                : maxDelay < baseDelay
                    ? new TextError($"Max delay {maxDelay} must not be shorter than base delay {baseDelay}")
                    : giveUpAfter < TimeSpan.Zero
                        ? new TextError($"Give-up time must not be negative: {giveUpAfter}")
                        : new IntegrationEventRetryPolicy
                        {
                            ImmediateAttempts = immediateAttempts,
                            BaseDelay = baseDelay,
                            MaxDelay = maxDelay,
                            GiveUpAfter = giveUpAfter
                        };

    public IntegrationEventRetryDecision Decide(int deferrals)
    {
        var waited = TimeSpan.Zero;
        var deferral = 0;

        while (deferral < deferrals && waited < GiveUpAfter)
        {
            deferral++;
            waited += DelayOf(deferral);
        }

        return waited >= GiveUpAfter
            ? new DeadLetterIntegrationEvent()
            : new DeferIntegrationEvent(DelayOf(deferral + 1));
    }

    private TimeSpan DelayOf(int deferral)
        => TimeSpan.FromTicks((long)Math.Min(BaseDelay.Ticks * Math.Pow(2, deferral - 1), MaxDelay.Ticks));
}
