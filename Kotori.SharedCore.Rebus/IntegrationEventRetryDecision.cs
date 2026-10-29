namespace Kotori.SharedCore.Rebus;

public abstract record IntegrationEventRetryDecision;

public sealed record DeferIntegrationEvent(TimeSpan Delay) : IntegrationEventRetryDecision;

public sealed record DeadLetterIntegrationEvent : IntegrationEventRetryDecision;
