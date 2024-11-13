namespace Kotori.SharedCore.UseCases;

public interface IUseCaseBus
{
    Task<TResponse> ExecuteQueryAsync<TQuery, TResponse>(TQuery query) where TQuery : IQuery;
    
    Task<TResult> ExecuteCommandAsync<TCommand, TResult>(TCommand command);
}