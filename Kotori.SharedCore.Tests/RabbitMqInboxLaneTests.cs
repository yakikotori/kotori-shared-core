using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using FluentAssertions;
using Kotori.SharedCore.DomainEvents;
using Kotori.SharedCore.EntityFrameworkCore;
using Kotori.SharedCore.EntityFrameworkCore.Inbox;
using Kotori.SharedCore.Inbox;
using Kotori.SharedCore.IntegrationEvents;
using Kotori.SharedCore.Rebus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Rebus.Config;
using Rebus.Persistence.InMem;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Kotori.SharedCore.Tests;

public class InboxLaneContainers : IAsyncLifetime
{
    public const string InboxTableSql =
        """
        create table inbox_messages ("MessageId" uuid not null, "Consumer" varchar(200) not null, "ProcessedOnUtc" timestamptz not null, constraint "PK_inbox_messages" primary key ("MessageId", "Consumer"));
        create index "IX_inbox_messages_ProcessedOnUtc" on inbox_messages ("ProcessedOnUtc");
        """;

    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public RabbitMqContainer RabbitMq { get; } = new RabbitMqBuilder()
        .WithImage("rabbitmq:4.1-management")
        .Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(Postgres.StartAsync(), RabbitMq.StartAsync());

        await using var context = new InboxTestDbContext(new DbContextOptionsBuilder<InboxTestDbContext>()
            .UseNpgsql(Postgres.GetConnectionString())
            .Options);

        await context.Database.ExecuteSqlRawAsync(InboxTableSql);
    }

    public async Task DisposeAsync()
    {
        await Postgres.DisposeAsync();
        await RabbitMq.DisposeAsync();
    }
}

public class RabbitMqInboxLaneTests : IClassFixture<InboxLaneContainers>, IAsyncLifetime
{
    private const string ErrorQueue = "inbox-lanes.error";

    private readonly InboxLaneContainers _containers;
    private readonly string _laneA = $"inbox-lane-a-{Guid.NewGuid():N}";
    private readonly string _laneB = $"inbox-lane-b-{Guid.NewGuid():N}";

    private readonly IntegrationEventRetryPolicy _quickRetry = IntegrationEventRetryPolicy
        .Create(2, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3))
        .Unwrap();

    private IHost _host = null!;

    public RabbitMqInboxLaneTests(InboxLaneContainers containers)
    {
        _containers = containers;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    private async Task StartAsync(Action<IHostBuilder> addLanes)
    {
        var builder = new HostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton<LaneCallLog>();
            services.AddSingleton(TimeProvider.System);
            services.AddDbContextFactory<InboxTestDbContext>(options => options.UseNpgsql(_containers.Postgres.GetConnectionString()));
            services.AddSingleton(Substitute.For<IDomainEventDispatcher>());
            services.AddSingleton<IUnitOfWorkFactory, EfUnitOfWorkFactory<InboxTestDbContext>>();
            services.AddInbox(new ConfigurationBuilder().Build());
            services.AddRebus(rebus => rebus.Transport(transport => transport
                .UseRabbitMqAsOneWayClient(_containers.RabbitMq.GetConnectionString())));
            services.AddRebusIntegrationEventDispatcher();
        });

        addLanes(builder);

        _host = builder.Build();

        await _host.StartAsync();
    }

    private Func<RebusConfigurer, RebusConfigurer> SerialRabbitMqLane(string queue)
        => rebus => rebus
            .Transport(transport => transport.UseRabbitMq(_containers.RabbitMq.GetConnectionString(), queue))
            .Timeouts(timeouts => timeouts.StoreInMemory())
            .Options(options =>
            {
                options.SetNumberOfWorkers(1);
                options.SetMaxParallelism(1);
            });

    private Task DispatchAsync(IIntegrationEvent integrationEvent)
        => _host.Services.GetRequiredService<IIntegrationEventDispatcher>().DispatchAsync(integrationEvent);

    private LaneCallLog CallLog => _host.Services.GetRequiredService<LaneCallLog>();

    private MetricCollector<long> Collect(string instrument)
        => new(_host.Services.GetRequiredService<IMeterFactory>(), KotoriSharedCoreMetrics.MeterName, instrument);

    private async Task<List<string>> ConsumersThatProcessedAsync(Guid messageId)
    {
        await using var context = await _host.Services
            .GetRequiredService<IDbContextFactory<InboxTestDbContext>>()
            .CreateDbContextAsync();

        return await context.Set<InboxMessageEntity>()
            .Where(message => message.MessageId == messageId)
            .Select(message => message.Consumer)
            .ToListAsync();
    }

    private async Task<List<string>> WaitForConsumersThatProcessedAsync(Guid messageId, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        while (true)
        {
            var consumers = await ConsumersThatProcessedAsync(messageId);

            if (consumers.Count >= count)
            {
                return consumers;
            }

            await Task.Delay(50, timeout.Token);
        }
    }

    [Fact]
    public async Task DuplicateOfAnEventRunsTheHandlerOnce()
    {
        await StartAsync(host => host.AddIntegrationEventLane(SerialRabbitMqLane(_laneA), ErrorQueue, _quickRetry, lane => lane
            .Handle<CrateShipped, LaneAShippedHandler>()
            .Handle<CrateReturned, LaneAReturnedHandler>()
            .Forward<LaneCallLog>()
            .UseInbox()));

        var shipped = new CrateShipped(Guid.NewGuid());
        var marker = new CrateReturned(Guid.NewGuid());

        await DispatchAsync(shipped);
        await DispatchAsync(shipped);
        await DispatchAsync(marker);
        await CallLog.WaitForEventAsync(marker);

        CallLog.Calls.Select(call => (call.Handler, call.Event)).Should().Equal(
            (nameof(LaneAShippedHandler), shipped),
            (nameof(LaneAReturnedHandler), marker));
        (await ConsumersThatProcessedAsync(shipped.Id)).Should().Equal(_laneA);
    }

    [Fact]
    public async Task LaneRecordsTheDuplicateItsInboxSkipped()
    {
        await StartAsync(host => host.AddIntegrationEventLane(SerialRabbitMqLane(_laneA), ErrorQueue, _quickRetry, lane => lane
            .Handle<CrateShipped, LaneAShippedHandler>()
            .Handle<CrateReturned, LaneAReturnedHandler>()
            .Forward<LaneCallLog>()
            .UseInbox()));

        using var skipped = Collect("kotori.inbox.duplicates.skipped");
        using var handled = Collect("kotori.lane.messages.handled");

        var shipped = new CrateShipped(Guid.NewGuid());

        await DispatchAsync(shipped);
        await DispatchAsync(shipped);
        await DispatchAsync(new CrateReturned(Guid.NewGuid()));
        await handled.WaitForMeasurementsAsync(2, TimeSpan.FromSeconds(30));

        skipped.GetMeasurementSnapshot().Should().ContainSingle().Which.Tags.Should().BeEquivalentTo(
            new Dictionary<string, object?> { ["event.type"] = nameof(CrateShipped), ["lane"] = _laneA });
        handled.GetMeasurementSnapshot().Select(measurement => measurement.Tags["event.type"]).Should().Equal(
            nameof(CrateShipped),
            nameof(CrateReturned));
    }

    [Fact]
    public async Task EventRunsOnEveryLaneThatUsesTheInbox()
    {
        await StartAsync(host => host
            .AddIntegrationEventLane(SerialRabbitMqLane(_laneA), ErrorQueue, _quickRetry, lane => lane
                .Handle<CrateShipped, LaneAShippedHandler>()
                .Forward<LaneCallLog>()
                .UseInbox())
            .AddIntegrationEventLane(SerialRabbitMqLane(_laneB), ErrorQueue, _quickRetry, lane => lane
                .Handle<CrateShipped, LaneBShippedHandler>()
                .Forward<LaneCallLog>()
                .UseInbox()));

        var shipped = new CrateShipped(Guid.NewGuid());

        await DispatchAsync(shipped);
        await CallLog.WaitForAsync(2);

        CallLog.Calls.Select(call => call.Handler).Should().BeEquivalentTo(
            nameof(LaneAShippedHandler),
            nameof(LaneBShippedHandler));
        (await WaitForConsumersThatProcessedAsync(shipped.Id, 2)).Should().BeEquivalentTo(_laneA, _laneB);
    }

    [Fact]
    public async Task EventAlreadyProcessedByOneLaneStillRunsOnTheOtherLane()
    {
        await StartAsync(host => host
            .AddIntegrationEventLane(SerialRabbitMqLane(_laneA), ErrorQueue, _quickRetry, lane => lane
                .Handle<CrateShipped, LaneAShippedHandler>()
                .Handle<CrateReturned, LaneAReturnedHandler>()
                .Forward<LaneCallLog>()
                .UseInbox())
            .AddIntegrationEventLane(SerialRabbitMqLane(_laneB), ErrorQueue, _quickRetry, lane => lane
                .Handle<CrateShipped, LaneBShippedHandler>()
                .Forward<LaneCallLog>()
                .UseInbox()));

        var shipped = new CrateShipped(Guid.NewGuid());
        var marker = new CrateReturned(Guid.NewGuid());

        await _host.Services.GetRequiredService<IInbox>().MarkProcessedAsync(shipped.Id, _laneA);

        await DispatchAsync(shipped);
        await DispatchAsync(marker);
        await CallLog.WaitForEventAsync(marker);
        await CallLog.WaitForEventAsync(shipped);

        CallLog.Calls.Select(call => (call.Handler, call.Event)).Should().BeEquivalentTo(new (string, IIntegrationEvent)[]
        {
            (nameof(LaneBShippedHandler), shipped),
            (nameof(LaneAReturnedHandler), marker)
        });
    }

    [Fact]
    public async Task FailedAttemptIsNotRecordedSoTheRetryRunsTheHandlerAgainAndRecordsTheSuccess()
    {
        await StartAsync(host => host.AddIntegrationEventLane(SerialRabbitMqLane(_laneA), ErrorQueue, _quickRetry, lane => lane
            .Handle<CrateShipped, FailingOnceShippedHandler>()
            .Forward<LaneCallLog>()
            .UseInbox()));

        var shipped = new CrateShipped(Guid.NewGuid());

        await DispatchAsync(shipped);
        await CallLog.WaitForAsync(2);

        CallLog.Calls.Select(call => call.Handler).Should().Equal(
            $"{nameof(FailingOnceShippedHandler)} failed, recorded before: False",
            $"{nameof(FailingOnceShippedHandler)} succeeded, recorded before: False");
        (await WaitForConsumersThatProcessedAsync(shipped.Id, 1)).Should().Equal(_laneA);
    }

    [Fact]
    public async Task LaneWithoutTheInboxRunsADuplicateAgain()
    {
        await StartAsync(host => host.AddIntegrationEventLane(SerialRabbitMqLane(_laneA), ErrorQueue, _quickRetry, lane => lane
            .Handle<CrateShipped, LaneAShippedHandler>()
            .Forward<LaneCallLog>()));

        var shipped = new CrateShipped(Guid.NewGuid());

        await DispatchAsync(shipped);
        await DispatchAsync(shipped);
        await CallLog.WaitForAsync(2);

        CallLog.Calls.Select(call => (call.Handler, call.Event)).Should().Equal(
            (nameof(LaneAShippedHandler), shipped),
            (nameof(LaneAShippedHandler), shipped));
        CallLog.Calls.Should().AllSatisfy(call => call.MessageId.Should().Be(shipped.Id.ToString()));
        (await ConsumersThatProcessedAsync(shipped.Id)).Should().BeEmpty();
    }
}

public class FailingOnceShippedHandler : IIntegrationEventHandler<CrateShipped>
{
    private readonly ConcurrentDictionary<Guid, int> _attempts = new();
    private readonly LaneCallLog _log;
    private readonly IInbox _inbox;
    private readonly IntegrationEventLaneConsumer _consumer;

    public FailingOnceShippedHandler(LaneCallLog log, IInbox inbox, IntegrationEventLaneConsumer consumer)
    {
        _log = log;
        _inbox = inbox;
        _consumer = consumer;
    }

    public async Task HandleAsync(CrateShipped integrationEvent, CancellationToken cancellationToken)
    {
        var recordedBefore = await _inbox.HasProcessedAsync(integrationEvent.Id, _consumer.Name, cancellationToken);
        var attempt = _attempts.AddOrUpdate(integrationEvent.Id, 1, (_, attempts) => attempts + 1);
        var outcome = attempt == 1 ? "failed" : "succeeded";

        _log.Add($"{nameof(FailingOnceShippedHandler)} {outcome}, recorded before: {recordedBefore}", integrationEvent, cancellationToken);

        if (attempt == 1)
        {
            throw new InvalidOperationException("Courier API timed out");
        }
    }
}
