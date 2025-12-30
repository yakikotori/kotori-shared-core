using Kotori.SharedCore.IntegrationEvents;
using MassTransit;

namespace Kotori.SharedCore.EntityFrameworkCore.MassTransit;

public class MassTransitIntegrationEventDispatcher : IIntegrationEventDispatcher
{
    private readonly IPublishEndpoint _publishEndpoint;

    public MassTransitIntegrationEventDispatcher(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public async Task DispatchAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        await _publishEndpoint.Publish(integrationEvent, integrationEvent.GetType(), cancellationToken);
    }
}