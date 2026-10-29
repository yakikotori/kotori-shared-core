using FluentAssertions;
using Kotori.SharedCore.Rebus;
using NSubstitute;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Extensions;
using Rebus.Messages;
using Rebus.Transport.InMem;

namespace Kotori.SharedCore.Tests;

public class RebusIntegrationEventDispatcherTests : IDisposable
{
    private const string Subscriber = "probe";

    private readonly InMemNetwork _network = new();
    private readonly IBus _bus;

    public RebusIntegrationEventDispatcherTests()
    {
        _bus = Configure.OneWayClient()
            .Transport(transport => transport.UseInMemoryTransportAsOneWayClient(_network))
            .Start();
    }

    public void Dispose() => _bus.Dispose();

    private void Subscribe<TEvent>()
        => _network.AddSubscriber(typeof(TEvent).GetSimpleAssemblyQualifiedName(), Subscriber);

    [Fact]
    public async Task RelayingTheSameEventTwicePublishesItUnderTheSameMessageId()
    {
        Subscribe<CrateShipped>();
        var dispatcher = new RebusIntegrationEventDispatcher(_bus);
        var shipped = new CrateShipped(Guid.NewGuid());

        await dispatcher.DispatchAsync(shipped);
        await dispatcher.DispatchAsync(shipped);

        _network.GetMessages(Subscriber)
            .Select(message => message.Headers[Headers.MessageId])
            .Should().Equal(shipped.Id.ToString(), shipped.Id.ToString());
    }

    [Fact]
    public async Task PublishesUnderTheTopicOfTheEventRuntimeType()
    {
        Subscribe<CrateReturned>();
        var dispatcher = new RebusIntegrationEventDispatcher(_bus);

        await dispatcher.DispatchAsync(new CrateShipped(Guid.NewGuid()));
        await dispatcher.DispatchAsync(new CrateReturned(Guid.NewGuid()));

        _network.GetMessages(Subscriber)
            .Select(message => message.Headers[Headers.Type])
            .Should().Equal(typeof(CrateReturned).GetSimpleAssemblyQualifiedName());
    }

    [Fact]
    public async Task StampsTheCorrelationIdOfACorrelatedEvent()
    {
        Subscribe<CrateLost>();
        var dispatcher = new RebusIntegrationEventDispatcher(_bus);
        var lost = new CrateLost(Guid.NewGuid(), Guid.NewGuid());

        await dispatcher.DispatchAsync(lost);

        _network.GetMessages(Subscriber).Should().ContainSingle()
            .Which.Headers[Headers.CorrelationId].Should().Be(lost.CorrelationId.ToString());
    }

    [Fact]
    public async Task PublishFailurePropagates()
    {
        var bus = Substitute.For<IBus>();
        bus.Publish(Arg.Any<object>(), Arg.Any<IDictionary<string, string>>())
            .Returns(Task.FromException(new InvalidOperationException("Did not receive publisher confirms")));
        var dispatcher = new RebusIntegrationEventDispatcher(bus);

        var dispatch = () => dispatcher.DispatchAsync(new CrateShipped(Guid.NewGuid()));

        await dispatch.Should().ThrowAsync<InvalidOperationException>().WithMessage("Did not receive publisher confirms");
    }
}
