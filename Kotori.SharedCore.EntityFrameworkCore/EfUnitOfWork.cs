using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kotori.SharedCore.EntityFrameworkCore;

public class EfUnitOfWork<TContext> : IUnitOfWork where TContext : DbContext
{
    private readonly TContext _dbContext;
    private readonly IServiceProvider _serviceProvider;
    
    private readonly ConcurrentDictionary<Type, IRepository> _repositories = new();

    public EfUnitOfWork(TContext dbContext, IServiceProvider serviceProvider)
    {
        _dbContext = dbContext;
        _serviceProvider = serviceProvider;
    }

    public TRepository GetRepository<TRepository>() where TRepository : class, IRepository
    {
        var type = typeof(TRepository);

        return (TRepository)_repositories.GetOrAdd(
            type, 
            _ => (IRepository)ActivatorUtilities.CreateInstance(_serviceProvider, type, _dbContext));
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _dbContext.SaveChangesAsync(ct);
    }
    
    public async ValueTask DisposeAsync()
    {
        _repositories.Clear();
        await _dbContext.DisposeAsync();
    }
}