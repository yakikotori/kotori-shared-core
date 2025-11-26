using Microsoft.EntityFrameworkCore;

namespace Kotori.SharedCore.EntityFrameworkCore;

public interface IRepositoryFactory<out TRepository> where TRepository : IRepository
{
    TRepository Create(DbContext context);
}