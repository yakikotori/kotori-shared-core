using System.Text.Json;
using FluentAssertions;
using Kotori.SharedCore.EntityFrameworkCore.Outbox;
using Kotori.SharedCore.IntegrationEvents;

namespace Kotori.SharedCore.Tests;

public class JsonEventSerializerTests
{
    private readonly JsonEventSerializer _serializer = new();

    [Fact]
    public void Serialize_ReturnsValidJson()
    {
        var testEvent = new TestIntegrationEvent { Message = "Hello" };

        var json = _serializer.Serialize(testEvent);

        var parsed = JsonDocument.Parse(json);
        parsed.RootElement.GetProperty("Message").GetString().Should().Be("Hello");
    }

    [Fact]
    public void Deserialize_WithInvalidType_ReturnsTextError()
    {
        var result = _serializer.Deserialize("NonExistent.Type, NonExistent", "{}");

        result.IsFail.Should().BeTrue();
    }

    [Fact]
    public void Deserialize_WithMalformedJson_ReturnsTextError()
    {
        var typeName = typeof(TestIntegrationEvent).AssemblyQualifiedName!;

        var result = _serializer.Deserialize(typeName, "not valid json");

        result.IsFail.Should().BeTrue();
    }

    [Fact]
    public void Deserialize_WithNonEventType_ReturnsTextError()
    {
        var typeName = typeof(string).AssemblyQualifiedName!;

        var result = _serializer.Deserialize(typeName, "\"test\"");

        result.IsFail.Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_SerializeAndDeserialize_ReturnsEquivalentEvent()
    {
        var originalEvent = new TestIntegrationEvent { Message = "Test message" };
        var typeName = typeof(TestIntegrationEvent).AssemblyQualifiedName!;

        var json = _serializer.Serialize(originalEvent);
        var result = _serializer.Deserialize(typeName, json);

        result.IsOk.Should().BeTrue();
        var deserializedEvent = result.Unwrap() as TestIntegrationEvent;
        deserializedEvent.Should().NotBeNull();
        deserializedEvent!.Message.Should().Be("Test message");
    }
}

public record TestIntegrationEvent : IntegrationEvent
{
    public string Message { get; init; } = string.Empty;
}