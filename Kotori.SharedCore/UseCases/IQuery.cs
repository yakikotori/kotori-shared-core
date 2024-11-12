namespace Kotori.SharedCore.UseCases;

public interface IQuery<in TQuery, TResponse>
{
    Task<TResponse> ExecuteAsync(TQuery query);
}