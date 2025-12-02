using Kotori.SharedCore.UseCases.Exceptions;

namespace Kotori.SharedCore.UseCases;

public class ServiceProviderUseCaseBus : IUseCaseBus
{
    private readonly IServiceProvider _serviceProvider;

    public ServiceProviderUseCaseBus(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task<TResponse> ExecuteQueryAsync<TQuery, TResponse>(TQuery query, CancellationToken ct = default) where TQuery : IQuery
    {
        if (_serviceProvider.GetService(typeof(IQueryHandler<TQuery, TResponse>)) is not IQueryHandler<TQuery, TResponse> queryService)
        {
            throw new QueryNotFoundException(typeof(TQuery), typeof(TResponse));
        }
        
        return queryService.ExecuteAsync(query, ct);
    }

    public Task<TResult> ExecuteCommandAsync<TCommand, TResult>(TCommand command, CancellationToken ct = default) where TCommand : ICommand
    {
        if (_serviceProvider.GetService(typeof(ICommandHandler<TCommand, TResult>)) is not ICommandHandler<TCommand, TResult> commandService)
        {
            throw new CommandNotFoundException(typeof(TCommand), typeof(TResult));
        }
        
        return commandService.ExecuteAsync(command, ct);
    }
}