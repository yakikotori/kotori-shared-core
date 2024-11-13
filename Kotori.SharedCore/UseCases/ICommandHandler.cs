namespace Kotori.SharedCore.UseCases;

public interface ICommandHandler<in TCommand, TResult>
{
    Task<TResult> ExecuteAsync(TCommand command);
}