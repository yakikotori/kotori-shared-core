using Kotori.SharedCore.UseCases;

namespace Kotori.SharedCore.Tests.UseCases;

public class TestCommandHandler : ICommandHandler<string, string>
{
    private readonly Func<string, string> _execute;

    public TestCommandHandler(Func<string, string> execute)
    {
        _execute = execute;
    }

    public Task<string> ExecuteAsync(string command)
        => Task.FromResult(_execute(command));
}