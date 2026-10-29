using Kotori.SharedCore.IntegrationEvents;
using Rebus.Bus;
using Rebus.Messages;

namespace Kotori.SharedCore.Rebus;

public class RebusIntegrationEventDispatcher : IIntegrationEventDispatcher
{
    private readonly IBus _bus;

    public RebusIntegrationEventDispatcher(IBus bus)
    {
        _bus = bus;
    }

    public Task DispatchAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => _bus.Publish(integrationEvent, HeadersOf(integrationEvent));

    private static Dictionary<string, string> HeadersOf(IIntegrationEvent integrationEvent)
        => integrationEvent is ICorrelatedEvent correlatedEvent
            ? new Dictionary<string, string>
            {
                [Headers.MessageId] = integrationEvent.Id.ToString(),
                [Headers.CorrelationId] = correlatedEvent.CorrelationId.ToString()
            }
            : new Dictionary<string, string>
            {
                [Headers.MessageId] = integrationEvent.Id.ToString()
            };
}
