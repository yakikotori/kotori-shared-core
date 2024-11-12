namespace Kotori.SharedCore.UseCases;

public interface ICommand<in TCommand, TResult>
{
    Task<TResult> ExecuteAsync(TCommand command);
}