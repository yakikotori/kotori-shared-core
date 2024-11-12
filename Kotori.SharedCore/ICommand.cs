namespace Kotori.SharedCore;

public interface ICommand<in TCommand, TResult>
{
    Task<TResult> ExecuteAsync(TCommand command);
}