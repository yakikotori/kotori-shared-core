using Kotori.SharedCore.UseCases;

namespace Kotori.SharedCore.Tests.UseCases;

public readonly record struct TestCommand(string Text) : ICommand;