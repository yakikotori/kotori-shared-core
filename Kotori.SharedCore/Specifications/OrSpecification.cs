namespace Kotori.SharedCore.Specifications;

public record OrSpecification(ISpecification First, ISpecification Second) : ISpecification
{
    public bool IsSatisfied()
        => First.IsSatisfied() || Second.IsSatisfied();
}