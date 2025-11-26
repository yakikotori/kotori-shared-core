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

    public EfUnitOfWork(TContext context, IServiceScope serviceScope, IDomainEventDispatcher domainEventDispatcher)
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

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
        
        var domainEvents = _context.ChangeTracker
            .Entries<EntityBase>()
            .Select(entry => entry.Entity.RegisteredDomainEvents)
            .OfType<IReadOnlyCollection<IDomainEvent>>()
            .SelectMany(events => events);
        
        await _domainEventDispatcher.DispatchManyAsync(domainEvents);
    }
    
    public async ValueTask DisposeAsync()
    {
        _repositories.Clear();
        _serviceScope.Dispose();
        await _context.DisposeAsync();
    }
}