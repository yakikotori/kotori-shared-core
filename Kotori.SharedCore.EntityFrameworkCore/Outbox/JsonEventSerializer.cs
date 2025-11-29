using System.Text.Json;
using Kotori.SharedCore.IntegrationEvents;

namespace Kotori.SharedCore.EntityFrameworkCore.Outbox;

public class JsonEventSerializer : IEventSerializer
{
    public string Serialize(IIntegrationEvent integrationEvent)
    {
        return JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType());
    }

    public Result<IIntegrationEvent, TextError> Deserialize(Type type, string serializedEvent)
    {
        if (JsonSerializer.Deserialize(serializedEvent, type) is not IIntegrationEvent domainEvent)
        {
            return new TextError("Failed to deserialize event");
        }

        return new Ok<IIntegrationEvent, TextError>(domainEvent);
    }
}