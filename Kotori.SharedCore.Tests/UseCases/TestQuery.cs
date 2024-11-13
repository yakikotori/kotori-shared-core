using Kotori.SharedCore.UseCases;

namespace Kotori.SharedCore.Tests.UseCases;

public record TestQuery(string Text) : IQuery;