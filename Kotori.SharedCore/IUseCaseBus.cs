namespace Kotori.SharedCore;

public interface IUseCaseBus
{
    Task<TResponse> ExecuteQueryAsync<TRequest, TResponse>(TRequest request);
    
    Task<TResult> ExecuteCommandAsync<TCommand, TResult>(TCommand command);
}