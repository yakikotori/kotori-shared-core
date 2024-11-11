namespace Kotori.SharedCore.Specifications;

public record AndSpecification(ISpecification First, ISpecification Second) : ISpecification
{
    public bool IsSatisfied()
        => First.IsSatisfied() && Second.IsSatisfied();
}