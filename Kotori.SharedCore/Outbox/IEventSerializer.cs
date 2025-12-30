using Kotori.SharedCore.IntegrationEvents;

namespace Kotori.SharedCore.Outbox;

public interface IEventSerializer
{
    string Serialize(IIntegrationEvent integrationEvent);
    
    Result<IIntegrationEvent, TextError> Deserialize(Type type, string serializedEvent);
}