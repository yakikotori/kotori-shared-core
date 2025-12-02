using Kotori.SharedCore.UseCases;

namespace Kotori.SharedCore.Tests.UseCases;

public class TestCommandHandler : ICommandHandler<TestCommand, string>
{
    private readonly Func<string, string> _execute;

    public TestCommandHandler(Func<string, string> execute)
    {
        _execute = execute;
    }

    public Task<string> ExecuteAsync(TestCommand command, CancellationToken ct = default)
        => Task.FromResult(_execute(command.Text));
}