namespace Kotori.SharedCore.UseCases;

public interface IUseCaseBus
{
    Task<TResponse> ExecuteQueryAsync<TQuery, TResponse>(TQuery request);
    
    Task<TResult> ExecuteCommandAsync<TCommand, TResult>(TCommand command);
}