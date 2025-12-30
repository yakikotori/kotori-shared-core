using Kotori.SharedCore.IntegrationEvents;
using MassTransit;

namespace Kotori.SharedCore.EntityFrameworkCore.MassTransit;

public class MassTransitIntegrationEventDispatcher : IIntegrationEventDispatcher
{
    private readonly IBus _bus;

    public MassTransitIntegrationEventDispatcher(IBus bus)
    {
        _bus = bus;
    }

    public async Task DispatchAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        await _bus.Publish(
            integrationEvent,
            integrationEvent.GetType(),
            context =>
            {
                context.MessageId = integrationEvent.Id;

                if (integrationEvent is ICorrelatedEvent correlatedEvent)
                {
                    context.CorrelationId = correlatedEvent.CorrelationId;
                }
            },
            cancellationToken);
    }
}