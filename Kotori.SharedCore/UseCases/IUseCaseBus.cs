namespace Kotori.SharedCore.UseCases;

public interface IUseCaseBus
{
    Task<TResponse> ExecuteQueryAsync<TQuery, TResponse>(TQuery query, CancellationToken ct = default) where TQuery : IQuery;
    
    Task<TResult> ExecuteCommandAsync<TCommand, TResult>(TCommand command, CancellationToken ct = default) where TCommand : ICommand;
}