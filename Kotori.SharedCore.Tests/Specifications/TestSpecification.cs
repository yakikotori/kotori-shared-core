using Kotori.SharedCore.Specifications;

namespace Kotori.SharedCore.Tests.Specifications;

public readonly record struct TestSpecification(bool Value) : ISpecification
{
    public bool IsSatisfied()
        => Value;
}