using Kotori.SharedCore.UseCases.Exceptions;

namespace Kotori.SharedCore.UseCases;

public class ServicesUseCaseBus : IUseCaseBus
{
    private readonly IServiceProvider _serviceProvider;

    public ServicesUseCaseBus(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task<TResponse> ExecuteQueryAsync<TQuery, TResponse>(TQuery query) where TQuery : IQuery
    {
        if (_serviceProvider.GetService(typeof(IQueryHandler<TQuery, TResponse>)) is not IQueryHandler<TQuery, TResponse> queryService)
        {
            throw new QueryNotFoundException(typeof(TQuery), typeof(TResponse));
        }
        
        return queryService.ExecuteAsync(query);
    }

    public Task<TResult> ExecuteCommandAsync<TCommand, TResult>(TCommand command) where TCommand : ICommand
    {
        if (_serviceProvider.GetService(typeof(ICommandHandler<TCommand, TResult>)) is not ICommandHandler<TCommand, TResult> commandService)
        {
            throw new CommandNotFoundException(typeof(TCommand), typeof(TResult));
        }
        
        return commandService.ExecuteAsync(command);
    }
}