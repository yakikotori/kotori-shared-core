namespace Kotori.SharedCore.Specifications;

public readonly record struct OrSpecification(ISpecification First, ISpecification Second) : ISpecification
{
    public bool IsSatisfied()
        => First.IsSatisfied() || Second.IsSatisfied();
}