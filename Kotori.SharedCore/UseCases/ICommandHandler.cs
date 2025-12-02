namespace Kotori.SharedCore.UseCases;

public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand
{
    Task<TResult> ExecuteAsync(TCommand command, CancellationToken ct = default);
}