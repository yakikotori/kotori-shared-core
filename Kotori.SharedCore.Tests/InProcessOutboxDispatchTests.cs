using FluentAssertions;
using Kotori.SharedCore.DomainEvents;
using Kotori.SharedCore.EntityFrameworkCore;
using Kotori.SharedCore.EntityFrameworkCore.Outbox;
using Kotori.SharedCore.IntegrationEvents;
using Kotori.SharedCore.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace Kotori.SharedCore.Tests;

public class InProcessOutboxDispatchTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private readonly DateTime _now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly TimeProvider _timeProvider = Substitute.For<TimeProvider>();

    private ServiceProvider _serviceProvider = null!;

    public async Task InitializeAsync()
    {
        _timeProvider.GetUtcNow().Returns(new DateTimeOffset(_now));

        await _postgres.StartAsync();

        _serviceProvider = CreateServiceProvider();

        await using var context = await CreateDbContextAsync();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private ServiceProvider CreateServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OutboxProcessor:BatchSize"] = "10"
            })
            .Build();

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDbContextFactory<OutboxTestDbContext>(options => options.UseNpgsql(_postgres.GetConnectionString()));
        services.AddSingleton(Substitute.For<IDomainEventDispatcher>());
        services.AddSingleton<IUnitOfWorkFactory, EfUnitOfWorkFactory<OutboxTestDbContext>>();
        services.AddSingleton(_timeProvider);
        services.AddOutbox(configuration);
        services.AddSingleton<IIntegrationEventDispatcher, ServiceProviderIntegrationEventDispatcher>();
        services.AddSingleton<ParcelShippedLog>();
        services.AddIntegrationEventHandler<ParcelShipped, ParcelShippedHandler>();

        return services.BuildServiceProvider();
    }

    private Task<OutboxTestDbContext> CreateDbContextAsync()
        => _serviceProvider.GetRequiredService<IDbContextFactory<OutboxTestDbContext>>().CreateDbContextAsync();

    private async Task AddMessageAsync(IIntegrationEvent integrationEvent)
    {
        await using var uow = await _serviceProvider.GetRequiredService<IUnitOfWorkFactory>().CreateAsync();

        uow.GetRepository<IOutboxMessageRepository>().Add(integrationEvent, _now);

        await uow.SaveChangesAsync();
    }

    private async Task<OutboxMessageEntity> GetMessageAsync()
    {
        await using var context = await CreateDbContextAsync();

        return await context.Set<OutboxMessageEntity>().SingleAsync();
    }

    private Task ProcessAsync() => _serviceProvider.GetRequiredService<IOutboxProcessor>().ProcessAsync();

    [Fact]
    public async Task ProcessAsync_DeliversEventToRegisteredHandler_AndMarksAsProcessed()
    {
        var parcelShipped = new ParcelShipped(Guid.NewGuid(), "Lisbon");

        await AddMessageAsync(parcelShipped);

        await ProcessAsync();

        _serviceProvider.GetRequiredService<ParcelShippedLog>().Events
            .Should().ContainSingle()
            .Which.Should().Be(parcelShipped);

        var message = await GetMessageAsync();
        message.State.Should().Be(OutboxMessageState.Processed);
        message.Attempts.Should().Be(1);
        message.ProcessedOnUtc.Should().Be(_now);
    }

    [Fact]
    public async Task ProcessAsync_SchedulesRetry_WhenNoHandlerRegistered()
    {
        await AddMessageAsync(new OutboxTestEvent());

        await ProcessAsync();

        var message = await GetMessageAsync();
        message.State.Should().Be(OutboxMessageState.Pending);
        message.Attempts.Should().Be(1);
        message.ErrorMessage.Should().Contain(typeof(OutboxTestEvent).FullName);
        _serviceProvider.GetRequiredService<ParcelShippedLog>().Events.Should().BeEmpty();
    }

    public record ParcelShipped(Guid ParcelId, string Destination) : IntegrationEvent;

    public class ParcelShippedLog
    {
        public List<ParcelShipped> Events { get; } = [];
    }

    public class ParcelShippedHandler : IIntegrationEventHandler<ParcelShipped>
    {
        private readonly ParcelShippedLog _log;

        public ParcelShippedHandler(ParcelShippedLog log)
        {
            _log = log;
        }

        public Task HandleAsync(ParcelShipped integrationEvent, CancellationToken cancellationToken)
        {
            _log.Events.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }
}