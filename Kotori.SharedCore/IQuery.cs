namespace Kotori.SharedCore;

public interface IQuery<in TQuery, TResponse>
{
    Task<TResponse> ExecuteAsync(TQuery query);
}