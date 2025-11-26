using Microsoft.EntityFrameworkCore;

namespace Kotori.SharedCore.EntityFrameworkCore;

public class EfUnitOfWorkFactory<TContext> : IUnitOfWorkFactory where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _dbContextFactory;
    private readonly IServiceProvider _serviceProvider;

    public EfUnitOfWorkFactory(IDbContextFactory<TContext> dbContextFactory, IServiceProvider serviceProvider)
    {
        _dbContextFactory = dbContextFactory;
        _serviceProvider = serviceProvider;
    }

    public async Task<IUnitOfWork> CreateAsync()
    {
        var db = await _dbContextFactory.CreateDbContextAsync();
        
        return new EfUnitOfWork<TContext>(db, _serviceProvider);
    }
}