namespace Kotori.SharedCore.IntegrationEvents;

public interface ICorrelatedEvent
{
    Guid CorrelationId { get; }
}