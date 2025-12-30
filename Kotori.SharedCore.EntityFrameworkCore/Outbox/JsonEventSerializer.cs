using System.Text.Json;
using Kotori.SharedCore.IntegrationEvents;
using Kotori.SharedCore.Outbox;

namespace Kotori.SharedCore.EntityFrameworkCore.Outbox;

public class JsonEventSerializer : IEventSerializer
{
    public string Serialize(IIntegrationEvent integrationEvent)
    {
        return JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType());
    }

    public Result<IIntegrationEvent, TextError> Deserialize(string type, string serializedEvent)
    {
        var eventType = Type.GetType(type);

        if (eventType is null)
        {
            return new TextError("Event type not found");
        }

        IIntegrationEvent? integrationEvent;

        try
        {
            integrationEvent = JsonSerializer.Deserialize(serializedEvent, eventType) as IIntegrationEvent;
        }
        catch (JsonException)
        {
            return new TextError("Failed to deserialize event");
        }

        if (integrationEvent is null)
        {
            return new TextError("Failed to deserialize event");
        }

        return new Ok<IIntegrationEvent, TextError>(integrationEvent);
    }
}