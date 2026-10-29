using System.Text;
using FluentAssertions;
using Kotori.SharedCore.IntegrationEvents;
using Kotori.SharedCore.Rebus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Rebus.Config;
using Headers = Rebus.Messages.Headers;
using Rebus.Persistence.InMem;
using Testcontainers.RabbitMq;

namespace Kotori.SharedCore.Tests;

public class RabbitMqIntegrationEventLaneTests : IAsyncLifetime
{
    private const string ErrorQueue = "lanes.error";

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder()
        .WithImage("rabbitmq:4.1-management")
        .Build();

    private readonly IntegrationEventRetryPolicy _quickRetry = IntegrationEventRetryPolicy
        .Create(2, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3))
        .Unwrap();

    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        await _rabbitMq.StartAsync();

        var builder = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<LaneCallLog>();
                services.AddRebus(rebus => rebus.Transport(transport => transport
                    .UseRabbitMqAsOneWayClient(_rabbitMq.GetConnectionString())));
                services.AddRebusIntegrationEventDispatcher();
            })
            .AddIntegrationEventLane(RabbitMqLane("lane-a"), ErrorQueue, _quickRetry, lane => lane
                .Handle<CrateShipped, LaneAShippedHandler>()
                .Handle<CrateReturned, LaneAReturnedHandler>()
                .Forward<LaneCallLog>())
            .AddIntegrationEventLane(RabbitMqLane("lane-b"), ErrorQueue, _quickRetry, lane => lane
                .Handle<CrateShipped, LaneBShippedHandler>()
                .Handle<CrateLost, FailingLostHandler>()
                .Forward<LaneCallLog>());

        _host = builder.Build();

        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        await _rabbitMq.DisposeAsync();
    }

    private Func<RebusConfigurer, RebusConfigurer> RabbitMqLane(string queue)
        => rebus => rebus
            .Transport(transport => transport.UseRabbitMq(_rabbitMq.GetConnectionString(), queue))
            .Timeouts(timeouts => timeouts.StoreInMemory());

    private Task DispatchAsync(IIntegrationEvent integrationEvent)
        => _host.Services.GetRequiredService<IIntegrationEventDispatcher>().DispatchAsync(integrationEvent);

    private LaneCallLog CallLog => _host.Services.GetRequiredService<LaneCallLog>();

    [Fact]
    public async Task EventPublishedThroughTheDispatcherReachesEachSubscribedLaneOnce()
    {
        var shipped = new CrateShipped(Guid.NewGuid());
        var returned = new CrateReturned(Guid.NewGuid());

        await DispatchAsync(shipped);
        await DispatchAsync(returned);
        await CallLog.WaitForAsync(3);

        CallLog.Calls.Select(call => (call.Handler, call.Event)).Should().BeEquivalentTo(new (string, IIntegrationEvent)[]
        {
            (nameof(LaneAShippedHandler), shipped),
            (nameof(LaneBShippedHandler), shipped),
            (nameof(LaneAReturnedHandler), returned)
        });
        CallLog.Calls.Where(call => call.Event == shipped)
            .Should().AllSatisfy(call => call.MessageId.Should().Be(shipped.Id.ToString()));
    }

    [Fact]
    public async Task FailingEventIsDeferredThroughTheTimeoutStoreAndDeadLetteredOnTheBroker()
    {
        var lost = new CrateLost(Guid.NewGuid(), Guid.NewGuid());

        await DispatchAsync(lost);
        await CallLog.WaitForAsync(6);

        CallLog.Calls.Select(call => call.DeferCount).Should().Equal("0", "0", "1", "1", "2", "2");

        var deadLetter = await GetDeadLetterAsync();
        deadLetter[Headers.MessageId].Should().Be(lost.Id.ToString());
        deadLetter[Headers.ErrorDetails].Should().Contain("Gave up after 2 deferrals").And.Contain("Crate is lost");
    }

    private async Task<Dictionary<string, string>> GetDeadLetterAsync()
    {
        var factory = new ConnectionFactory { Uri = new Uri(_rabbitMq.GetConnectionString()) };

        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        while (true)
        {
            var result = await channel.BasicGetAsync(ErrorQueue, autoAck: true, timeout.Token);

            if (result is not null)
            {
                return result.BasicProperties.Headers!.ToDictionary(
                    header => header.Key,
                    header => header.Value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : $"{header.Value}");
            }

            await Task.Delay(100, timeout.Token);
        }
    }
}

public class FailingLostHandler : IIntegrationEventHandler<CrateLost>
{
    private readonly LaneCallLog _log;

    public FailingLostHandler(LaneCallLog log)
    {
        _log = log;
    }

    public Task HandleAsync(CrateLost integrationEvent, CancellationToken cancellationToken)
    {
        _log.Add(nameof(FailingLostHandler), integrationEvent, cancellationToken);
        throw new InvalidOperationException("Crate is lost");
    }
}
