namespace Kotori.SharedCore;

public interface IUnitOfWorkFactory
{
    Task<IUnitOfWork> CreateAsync();
}