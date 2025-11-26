namespace Kotori.SharedCore;

public interface IUnitOfWork : IAsyncDisposable
{
    T GetRepository<T>() where T : class, IRepository;
    
    Task SaveChangesAsync(CancellationToken ct = default);
}