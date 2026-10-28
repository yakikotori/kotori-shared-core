namespace Kotori.SharedCore.IntegrationEvents;

public record IntegrationEvent : IIntegrationEvent
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
}