namespace Kotori.SharedCore.IntegrationEvents;

public record IntegrationEvent : IIntegrationEvent
{
    public Guid Id { get; } = Guid.CreateVersion7();
}