namespace Kotori.SharedCore.UseCases;

public interface IQueryHandler<in TQuery, TResponse> where TQuery : IQuery
{
    Task<TResponse> ExecuteAsync(TQuery query, CancellationToken ct = default);
}