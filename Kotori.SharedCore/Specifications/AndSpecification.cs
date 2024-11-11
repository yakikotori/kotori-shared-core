namespace Kotori.SharedCore.Specifications;

public readonly record struct AndSpecification(ISpecification First, ISpecification Second) : ISpecification
{
    public bool IsSatisfied()
        => First.IsSatisfied() && Second.IsSatisfied();
}