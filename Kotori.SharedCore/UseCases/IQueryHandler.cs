namespace Kotori.SharedCore.UseCases;

public interface IQueryHandler<in TQuery, TResponse>
{
    Task<TResponse> ExecuteAsync(TQuery query);
}