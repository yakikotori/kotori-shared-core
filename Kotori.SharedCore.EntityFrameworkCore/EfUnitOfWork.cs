using System.Collections.Concurrent;
using Kotori.SharedCore.DomainEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kotori.SharedCore.EntityFrameworkCore;

public class EfUnitOfWork<TContext> : IUnitOfWork where TContext : DbContext
{
    private readonly TContext _context;
    private readonly IServiceScope _serviceScope;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    
    private readonly ConcurrentDictionary<Type, IRepository> _repositories = new();

    public EfUnitOfWork(
        TContext context,
        IServiceScope serviceScope,
        IDomainEventDispatcher domainEventDispatcher)
    {
        _context = context;
        _serviceScope = serviceScope;
        _domainEventDispatcher = domainEventDispatcher;
    }

    public TRepository GetRepository<TRepository>() where TRepository : class, IRepository
    {
        return (TRepository)_repositories.GetOrAdd(typeof(TRepository), _ =>
        {
            var repositoryFactory = _serviceScope.ServiceProvider.GetRequiredService<IRepositoryFactory<TRepository>>();

            return repositoryFactory.Create(_context);
        });
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    { 
        await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entities = _context.ChangeTracker
            .Entries<EntityBase>()
            .Select(entry => entry.Entity)
            .ToList();
        
        var domainEvents = entities
            .Select(entry => entry.RegisteredDomainEvents)
            .OfType<IReadOnlyCollection<IDomainEvent>>()
            .SelectMany(events => events)
            .ToList();

        var domainEventContext = new DomainEventContext
        {
            UnitOfWork = this,
            CancellationToken = cancellationToken
        };
        
        foreach (var domainEvent in domainEvents)
        {
            await _domainEventDispatcher.DispatchAsync(domainEvent, domainEventContext);
        }
        
        await _context.SaveChangesAsync(cancellationToken);

        if (_context.Database.CurrentTransaction is not null)
        {
            await _context.Database.CommitTransactionAsync(cancellationToken);
        }
        
        foreach (var entity in entities)
        {
            entity.ClearDomainEvents();
        }
    }
    
    public async ValueTask DisposeAsync()
    {
        _repositories.Clear();
        
        _serviceScope.Dispose();
        
        if (_context.Database.CurrentTransaction is not null)
        {
            await _context.Database.RollbackTransactionAsync();
        }
        
        await _context.DisposeAsync();
    }
}