namespace Kotori.SharedCore;

public interface IUnitOfWork : IAsyncDisposable
{
    T GetRepository<T>() where T : class, IRepository;
    
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}