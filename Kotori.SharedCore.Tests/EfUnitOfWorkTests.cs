using FluentAssertions;
using Kotori.SharedCore.DomainEvents;
using Kotori.SharedCore.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace Kotori.SharedCore.Tests;

public class EfUnitOfWorkTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = CreateDbContext();
        await context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private TestDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new TestDbContext(options);
    }

    private static EfUnitOfWork<TestDbContext> CreateUnitOfWork(
        TestDbContext context,
        IDomainEventDispatcher? dispatcher = null,
        IServiceScope? serviceScope = null)
    {
        serviceScope ??= Substitute.For<IServiceScope>();
        dispatcher ??= Substitute.For<IDomainEventDispatcher>();
        return new EfUnitOfWork<TestDbContext>(context, serviceScope, dispatcher);
    }

    [Fact]
    public async Task GetRepository_ReturnsSameInstance_WhenCalledMultipleTimes()
    {
        var context = CreateDbContext();
        var serviceScope = Substitute.For<IServiceScope>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        var repositoryFactory = Substitute.For<IRepositoryFactory<ITestRepository>>();
        var repository = Substitute.For<ITestRepository>();

        serviceScope.ServiceProvider.Returns(serviceProvider);
        serviceProvider.GetService(typeof(IRepositoryFactory<ITestRepository>)).Returns(repositoryFactory);
        repositoryFactory.Create(context).Returns(repository);

        await using var uow = CreateUnitOfWork(context, serviceScope: serviceScope);

        var repo1 = uow.GetRepository<ITestRepository>();
        var repo2 = uow.GetRepository<ITestRepository>();

        repo1.Should().BeSameAs(repo2);
        repositoryFactory.Received(1).Create(context);
    }

    [Fact]
    public async Task SaveChangesAsync_DispatchesDomainEvents()
    {
        var context = CreateDbContext();
        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        await using var uow = CreateUnitOfWork(context, dispatcher);

        var entity = new TestEntity { Id = Guid.NewGuid(), Name = "Test" };
        var domainEvent = new TestDomainEvent();
        entity.RegisterDomainEvent(domainEvent);
        context.TestEntities.Add(entity);

        await uow.SaveChangesAsync();

        await dispatcher.Received(1).DispatchAsync(
            domainEvent,
            Arg.Is<DomainEventContext>(ctx => ctx.UnitOfWork == uow));
    }

    [Fact]
    public async Task SaveChangesAsync_ClearsDomainEvents_AfterSave()
    {
        var context = CreateDbContext();
        await using var uow = CreateUnitOfWork(context);

        var entity = new TestEntity { Id = Guid.NewGuid(), Name = "Test" };
        entity.RegisterDomainEvent(new TestDomainEvent());
        context.TestEntities.Add(entity);

        await uow.SaveChangesAsync();

        entity.RegisteredDomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveChangesAsync_CommitsTransaction_WhenTransactionExists()
    {
        var context = CreateDbContext();
        await using var uow = CreateUnitOfWork(context);

        await uow.BeginTransactionAsync();

        var entity = new TestEntity { Id = Guid.NewGuid(), Name = "Test" };
        context.TestEntities.Add(entity);

        await uow.SaveChangesAsync();

        context.Database.CurrentTransaction.Should().BeNull();

        await using var verifyContext = CreateDbContext();
        var savedEntity = await verifyContext.TestEntities.FindAsync(entity.Id);
        savedEntity.Should().NotBeNull();
    }

    [Fact]
    public async Task DisposeAsync_RollsBackTransaction_WhenNotCommitted()
    {
        var context = CreateDbContext();
        var entityId = Guid.NewGuid();

        var uow = CreateUnitOfWork(context);
        await uow.BeginTransactionAsync();

        var entity = new TestEntity { Id = entityId, Name = "Test" };
        context.TestEntities.Add(entity);
        await context.SaveChangesAsync();

        await uow.DisposeAsync();

        await using var verifyContext = CreateDbContext();
        var savedEntity = await verifyContext.TestEntities.FindAsync(entityId);
        savedEntity.Should().BeNull();
    }

    [Fact]
    public async Task SaveChangesAsync_ThrowsAndDoesNotSave_WhenDispatchFails()
    {
        var context = CreateDbContext();
        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        dispatcher.DispatchAsync(Arg.Any<IDomainEvent>(), Arg.Any<DomainEventContext>())
            .Returns(Task.FromException(new InvalidOperationException("Dispatch failed")));

        var entityId = Guid.NewGuid();
        await using var uow = CreateUnitOfWork(context, dispatcher);
        await uow.BeginTransactionAsync();

        var entity = new TestEntity { Id = entityId, Name = "Test" };
        entity.RegisterDomainEvent(new TestDomainEvent());
        context.TestEntities.Add(entity);

        var act = () => uow.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();

        await using var verifyContext = CreateDbContext();
        var savedEntity = await verifyContext.TestEntities.FindAsync(entityId);
        savedEntity.Should().BeNull();
    }
    
    [Fact]
    public async Task BeginTransactionAsync_StartsTransaction()
    {
        var context = CreateDbContext();
        await using var uow = CreateUnitOfWork(context);
    
        context.Database.CurrentTransaction.Should().BeNull();
    
        await uow.BeginTransactionAsync();
    
        context.Database.CurrentTransaction.Should().NotBeNull();
    }
}

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    public DbSet<TestEntity> TestEntities => Set<TestEntity>();
}

public class TestEntity : EntityBase
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public record TestDomainEvent : IDomainEvent;

public interface ITestRepository : IRepository;