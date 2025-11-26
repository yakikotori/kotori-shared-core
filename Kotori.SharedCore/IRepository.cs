namespace Kotori.SharedCore;

public interface IRepository;

public interface IRepository<T> : IRepository where T : IAggregateRoot;