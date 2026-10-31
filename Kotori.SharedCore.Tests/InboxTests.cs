using FluentAssertions;
using Kotori.SharedCore.DomainEvents;
using Kotori.SharedCore.EntityFrameworkCore;
using Kotori.SharedCore.EntityFrameworkCore.Inbox;
using Kotori.SharedCore.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace Kotori.SharedCore.Tests;

public class InboxTests : IAsyncLifetime
{
    private const string LaneA = "shop.notifications";
    private const string LaneB = "shop.accounting";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private readonly TimeProvider _timeProvider = Substitute.For<TimeProvider>();
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
                ["InboxCleaner:ClearProcessedAfter"] = "1.00:00:00"
            })
            .Build();

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDbContextFactory<InboxTestDbContext>(options => options.UseNpgsql(_postgres.GetConnectionString()));
        services.AddSingleton(Substitute.For<IDomainEventDispatcher>());
        services.AddSingleton<IUnitOfWorkFactory, EfUnitOfWorkFactory<InboxTestDbContext>>();
        services.AddSingleton(_timeProvider);
        services.AddInbox(configuration);

        return services.BuildServiceProvider();
    }

    private Task<InboxTestDbContext> CreateDbContextAsync()
        => _serviceProvider.GetRequiredService<IDbContextFactory<InboxTestDbContext>>().CreateDbContextAsync();

    private IInbox Inbox => _serviceProvider.GetRequiredService<IInbox>();

    private async Task AddAsync(Guid messageId, string consumer, DateTime processedOnUtc)
    {
        await using var uow = await _serviceProvider.GetRequiredService<IUnitOfWorkFactory>().CreateAsync();

        await uow.GetRepository<IInboxMessageRepository>().AddAsync(messageId, consumer, processedOnUtc);

        await uow.SaveChangesAsync();
    }

    private async Task<bool> ContainsAsync(Guid messageId, string consumer)
    {
        await using var uow = await _serviceProvider.GetRequiredService<IUnitOfWorkFactory>().CreateAsync();

        return await uow.GetRepository<IInboxMessageRepository>().ContainsAsync(messageId, consumer);
    }

    private async Task<List<InboxMessageEntity>> GetMessagesAsync()
    {
        await using var context = await CreateDbContextAsync();

        return await context.Set<InboxMessageEntity>().ToListAsync();
    }

    [Fact]
    public async Task AddAsync_ThenContainsAsync_FindsOnlyThatMessageForThatConsumer()
    {
        var messageId = Guid.NewGuid();

        await AddAsync(messageId, LaneA, _start);

        (await ContainsAsync(messageId, LaneA)).Should().BeTrue();
        (await ContainsAsync(messageId, LaneB)).Should().BeFalse();
        (await ContainsAsync(Guid.NewGuid(), LaneA)).Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_OfADuplicate_DoesNotThrowAndKeepsTheFirstRow()
    {
        var messageId = Guid.NewGuid();

        await AddAsync(messageId, LaneA, _start);

        var addAgain = () => AddAsync(messageId, LaneA, _start.AddMinutes(5));

        await addAgain.Should().NotThrowAsync();
        (await GetMessagesAsync()).Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            MessageId = messageId,
            Consumer = LaneA,
            ProcessedOnUtc = _start
        });
    }

    [Fact]
    public async Task MarkProcessedAsync_OfConcurrentCopies_DoesNotThrowAndKeepsOneRow()
    {
        var messageId = Guid.NewGuid();

        var markCopies = () => Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Inbox.MarkProcessedAsync(messageId, LaneA)));

        await markCopies.Should().NotThrowAsync();
        (await GetMessagesAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task AddAsync_OfTheSameMessageForTwoConsumers_KeepsTwoRows()
    {
        var messageId = Guid.NewGuid();

        await AddAsync(messageId, LaneA, _start);
        await AddAsync(messageId, LaneB, _start);

        (await GetMessagesAsync()).Select(message => (message.MessageId, message.Consumer))
            .Should().BeEquivalentTo(new[] { (messageId, LaneA), (messageId, LaneB) });
    }

    [Fact]
    public async Task MarkProcessedAsync_RecordsTheCurrentTime_AndHasProcessedAsyncFindsIt()
    {
        var messageId = Guid.NewGuid();
        _now = _start.AddHours(3);

        (await Inbox.HasProcessedAsync(messageId, LaneA)).Should().BeFalse();

        await Inbox.MarkProcessedAsync(messageId, LaneA);

        (await Inbox.HasProcessedAsync(messageId, LaneA)).Should().BeTrue();
        (await Inbox.HasProcessedAsync(messageId, LaneB)).Should().BeFalse();
        (await GetMessagesAsync()).Should().ContainSingle().Which.ProcessedOnUtc.Should().Be(_start.AddHours(3));
    }

    [Fact]
    public async Task ClearAsync_RemovesOnlyMessagesProcessedBeforeClearProcessedAfter()
    {
        var old = Guid.NewGuid();
        var recent = Guid.NewGuid();

        await AddAsync(old, LaneA, _start.AddDays(-2));
        await AddAsync(old, LaneB, _start.AddHours(-25));
        await AddAsync(recent, LaneA, _start.AddHours(-23));

        await _serviceProvider.GetRequiredService<IInboxCleaner>().ClearAsync();

        (await GetMessagesAsync()).Select(message => (message.MessageId, message.Consumer))
            .Should().BeEquivalentTo(new[] { (recent, LaneA) });
    }
}

public class InboxServiceCollectionExtensionsTests
{
    private static ServiceProvider CreateServiceProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();

        services.AddInbox(configuration);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddInbox_UsesDefaultCleanerOptions_WhenSectionsMissing()
    {
        using var serviceProvider = CreateServiceProvider([]);

        serviceProvider.GetRequiredService<IOptions<InboxCleanerOptions>>().Value.ClearProcessedAfter
            .Should().Be(TimeSpan.FromDays(7));
        serviceProvider.GetRequiredService<IOptions<BackgroundInboxCleanerOptions>>().Value.DelayMs
            .Should().Be(60 * 1000);
    }

    [Fact]
    public void AddInbox_BindsCleanerOptions_WhenSectionsPresent()
    {
        using var serviceProvider = CreateServiceProvider(new Dictionary<string, string?>
        {
            ["InboxCleaner:ClearProcessedAfter"] = "2.00:00:00",
            ["BackgroundInboxCleaner:DelayMs"] = "4321"
        });

        serviceProvider.GetRequiredService<IOptions<InboxCleanerOptions>>().Value.ClearProcessedAfter
            .Should().Be(TimeSpan.FromDays(2));
        serviceProvider.GetRequiredService<IOptions<BackgroundInboxCleanerOptions>>().Value.DelayMs
            .Should().Be(4321);
    }
}

public class InboxTestDbContext : DbContext
{
    public InboxTestDbContext(DbContextOptions<InboxTestDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new InboxMessageEntityTypeConfiguration());
    }
}
