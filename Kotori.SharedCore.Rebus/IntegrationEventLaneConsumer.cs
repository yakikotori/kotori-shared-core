namespace Kotori.SharedCore.Rebus;

public sealed class IntegrationEventLaneConsumer
{
    internal IntegrationEventLaneConsumer(string name)
    {
        Name = name;
    }

    public string Name { get; }
}
