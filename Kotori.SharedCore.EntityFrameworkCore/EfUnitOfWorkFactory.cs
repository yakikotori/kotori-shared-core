using Kotori.SharedCore.DomainEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kotori.SharedCore.EntityFrameworkCore;

public class EfUnitOfWorkFactory<TContext> : IUnitOfWorkFactory where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _dbContextFactory;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IDomainEventDispatcher _domainEventDispatcher;

    public EfUnitOfWorkFactory(IDbContextFactory<TContext> dbContextFactory, IServiceScopeFactory serviceScopeFactory, IDomainEventDispatcher domainEventDispatcher)
    {
        _dbContextFactory = dbContextFactory;
        _serviceScopeFactory = serviceScopeFactory;
        _domainEventDispatcher = domainEventDispatcher;
    }

    public async Task<IUnitOfWork> CreateAsync()
    {
        var context = await _dbContextFactory.CreateDbContextAsync();
        
        var serviceScope = _serviceScopeFactory.CreateScope();
        
        return new EfUnitOfWork<TContext>(context, serviceScope, _domainEventDispatcher);
    }
}