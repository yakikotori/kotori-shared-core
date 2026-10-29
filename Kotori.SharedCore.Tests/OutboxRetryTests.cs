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

public class OutboxRetryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private readonly TimeProvider _timeProvider = Substitute.For<TimeProvider>();
    private readonly IIntegrationEventDispatcher _integrationEventDispatcher = Substitute.For<IIntegrationEventDispatcher>();
    private readonly DateTime _start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private DateTime _now;
    private ServiceProvider _serviceProvider = null!;

    public async Task InitializeAsync()
    {
        _now = _start;
        _timeProvider.GetUtcNow().Returns(_ => new DateTimeOffset(_now));

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
                ["OutboxProcessor:BatchSize"] = "10",
                ["OutboxProcessor:MaxAttempts"] = "3",
                ["OutboxProcessor:RetryBaseDelay"] = "00:00:10",
                ["OutboxProcessor:RetryMaxDelay"] = "00:01:00",
                ["OutboxCleaner:ClearProcessedAfter"] = "1.00:00:00",
                ["OutboxCleaner:ClearFailedAfter"] = "7.00:00:00"
            })
            .Build();

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDbContextFactory<OutboxTestDbContext>(options => options.UseNpgsql(_postgres.GetConnectionString()));
        services.AddSingleton(Substitute.For<IDomainEventDispatcher>());
        services.AddSingleton<IUnitOfWorkFactory, EfUnitOfWorkFactory<OutboxTestDbContext>>();
        services.AddSingleton(_timeProvider);
        services.AddSingleton(_integrationEventDispatcher);
        services.AddOutbox(configuration);

        return services.BuildServiceProvider();
    }

    private Task<OutboxTestDbContext> CreateDbContextAsync()
        => _serviceProvider.GetRequiredService<IDbContextFactory<OutboxTestDbContext>>().CreateDbContextAsync();

    private async Task AddMessageAsync()
    {
        await using var uow = await _serviceProvider.GetRequiredService<IUnitOfWorkFactory>().CreateAsync();

        uow.GetRepository<IOutboxMessageRepository>().Add(new OutboxTestEvent(), _now);

        await uow.SaveChangesAsync();
    }

    private async Task<OutboxMessageEntity> GetMessageAsync()
    {
        await using var context = await CreateDbContextAsync();

        return await context.Set<OutboxMessageEntity>().SingleAsync();
    }

    private Task ProcessAsync() => _serviceProvider.GetRequiredService<IOutboxProcessor>().ProcessAsync();

    [Fact]
    public async Task ProcessAsync_RetriesFailedDispatch_OnlyAfterBackoffPassed()
    {
        _integrationEventDispatcher
            .DispatchAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("Notification API unavailable")));

        await AddMessageAsync();

        await ProcessAsync();

        var message = await GetMessageAsync();
        message.State.Should().Be(OutboxMessageState.Pending);
        message.Attempts.Should().Be(1);
        message.ErrorMessage.Should().Be("Notification API unavailable");
        message.NextAttemptOnUtc.Should().Be(_start.AddSeconds(10));

        _now = _start.AddSeconds(9);
        await ProcessAsync();

        await _integrationEventDispatcher.Received(1)
            .DispatchAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<CancellationToken>());
        (await GetMessageAsync()).Attempts.Should().Be(1);

        _now = _start.AddSeconds(10);
        await ProcessAsync();

        await _integrationEventDispatcher.Received(2)
            .DispatchAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<CancellationToken>());

        message = await GetMessageAsync();
        message.State.Should().Be(OutboxMessageState.Pending);
        message.Attempts.Should().Be(2);
        message.NextAttemptOnUtc.Should().Be(_start.AddSeconds(30));
    }

    [Fact]
    public async Task ProcessAsync_TruncatesLongErrorMessage_SoTheBatchStillSaves()
    {
        var longMessage = new string('x', OutboxMessageEntity.MaxErrorMessageLength + 500);

        _integrationEventDispatcher
            .DispatchAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException(longMessage)));

        await AddMessageAsync();

        await ProcessAsync();

        var message = await GetMessageAsync();
        message.State.Should().Be(OutboxMessageState.Pending);
        message.Attempts.Should().Be(1);
        message.ErrorMessage.Should().HaveLength(OutboxMessageEntity.MaxErrorMessageLength);
    }

    [Fact]
    public async Task ProcessAsync_MarksAsFailed_OnlyAfterMaxAttempts()
    {
        _integrationEventDispatcher
            .DispatchAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("Notification API unavailable")));

        await AddMessageAsync();

        await ProcessAsync();
        (await GetMessageAsync()).State.Should().Be(OutboxMessageState.Pending);

        _now = _start.AddSeconds(10);
        await ProcessAsync();
        (await GetMessageAsync()).State.Should().Be(OutboxMessageState.Pending);

        _now = _start.AddSeconds(30);
        await ProcessAsync();

        var message = await GetMessageAsync();
        message.State.Should().Be(OutboxMessageState.Failed);
        message.Attempts.Should().Be(3);
        message.ProcessedOnUtc.Should().Be(_start.AddSeconds(30));
        message.ErrorMessage.Should().Be("Notification API unavailable");

        _now = _start.AddHours(1);
        await ProcessAsync();

        await _integrationEventDispatcher.Received(3)
            .DispatchAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_MarksAsProcessed_WhenLaterAttemptSucceeds()
    {
        _integrationEventDispatcher
            .DispatchAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException(new HttpRequestException("Notification API unavailable")),
                Task.CompletedTask);

        await AddMessageAsync();

        await ProcessAsync();
        (await GetMessageAsync()).State.Should().Be(OutboxMessageState.Pending);

        _now = _start.AddSeconds(10);
        await ProcessAsync();

        var message = await GetMessageAsync();
        message.State.Should().Be(OutboxMessageState.Processed);
        message.Attempts.Should().Be(2);
        message.ProcessedOnUtc.Should().Be(_start.AddSeconds(10));
    }

    [Fact]
    public async Task ProcessAsync_SavesTheInterruptedBatch_WhenShutdownIsRequestedMidBatch()
    {
        using var shutdown = new CancellationTokenSource();

        _integrationEventDispatcher
            .DispatchAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<CancellationToken>())
            .Returns(
                _ =>
                {
                    shutdown.Cancel();
                    return Task.CompletedTask;
                },
                _ => Task.CompletedTask);

        await AddMessageAsync();
        _now = _start.AddSeconds(1);
        await AddMessageAsync();

        await _serviceProvider.GetRequiredService<IOutboxProcessor>().ProcessAsync(shutdown.Token);

        await using var context = await CreateDbContextAsync();
        var messages = await context.Set<OutboxMessageEntity>()
            .OrderBy(message => message.OccurredOnUtc)
            .ToListAsync();

        messages.Select(message => message.State).Should().Equal(OutboxMessageState.Processed, OutboxMessageState.Pending);
        messages.Select(message => message.Attempts).Should().Equal(1, 0);
        await _integrationEventDispatcher.Received(1)
            .DispatchAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClearAsync_RemovesOldProcessedAndFailed_AndKeepsRetryingMessages()
    {
        var oldProcessed = OutboxMessageEntity.Create("Processed", "{}", _start.AddDays(-3));
        oldProcessed.MarkAsProcessed(_start.AddDays(-2));

        var recentProcessed = OutboxMessageEntity.Create("RecentProcessed", "{}", _start.AddHours(-2));
        recentProcessed.MarkAsProcessed(_start.AddHours(-1));

        var oldFailed = OutboxMessageEntity.Create("Failed", "{}", _start.AddDays(-9));
        oldFailed.MarkAsFailed(_start.AddDays(-8), "Dispatch failed");

        var retrying = OutboxMessageEntity.Create("Retrying", "{}", _start.AddDays(-30));
        retrying.ScheduleRetry(_start.AddMinutes(1), "Dispatch failed");

        await using (var context = await CreateDbContextAsync())
        {
            context.Set<OutboxMessageEntity>().AddRange(oldProcessed, recentProcessed, oldFailed, retrying);
            await context.SaveChangesAsync();
        }

        await _serviceProvider.GetRequiredService<IOutboxCleaner>().ClearAsync();

        await using var verifyContext = await CreateDbContextAsync();
        var remainingTypes = await verifyContext.Set<OutboxMessageEntity>()
            .Select(message => message.Type)
            .ToListAsync();

        remainingTypes.Should().BeEquivalentTo("RecentProcessed", "Retrying");
    }
}

public class OutboxTestDbContext : DbContext
{
    public OutboxTestDbContext(DbContextOptions<OutboxTestDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxMessageEntityTypeConfiguration());
    }
}

public record OutboxTestEvent : IntegrationEvent;