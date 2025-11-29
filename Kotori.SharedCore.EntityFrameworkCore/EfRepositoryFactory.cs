using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kotori.SharedCore.EntityFrameworkCore;

public class EfRepositoryFactory<TRepository> : IRepositoryFactory<TRepository> where TRepository : IRepository
{
    private readonly IServiceProvider _serviceProvider;

    public EfRepositoryFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public TRepository Create(DbContext context)
        => ActivatorUtilities.CreateInstance<TRepository>(_serviceProvider, context);
}