using Kotori.SharedCore.UseCases.Exceptions;

namespace Kotori.SharedCore.UseCases;

public class ServicesUseCaseBus : IUseCaseBus
{
    private readonly IServiceProvider _serviceProvider;

    public ServicesUseCaseBus(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task<TResponse> ExecuteQueryAsync<TQuery, TResponse>(TQuery query)
    {
        if (_serviceProvider.GetService(typeof(IQuery<TQuery, TResponse>)) is not IQuery<TQuery, TResponse> queryService)
        {
            throw new QueryNotFoundException(typeof(TQuery), typeof(TResponse));
        }
        
        return queryService.ExecuteAsync(query);
    }

    public Task<TResult> ExecuteCommandAsync<TCommand, TResult>(TCommand command)
    {
        if (_serviceProvider.GetService(typeof(ICommand<TCommand, TResult>)) is not ICommand<TCommand, TResult> commandService)
        {
            throw new CommandNotFoundException(typeof(TCommand), typeof(TResult));
        }
        
        return commandService.ExecuteAsync(command);
    }
}