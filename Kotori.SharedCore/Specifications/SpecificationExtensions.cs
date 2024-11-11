namespace Kotori.SharedCore.Specifications;

public static class SpecificationExtensions
{
    public static ISpecification And(this ISpecification first, ISpecification second)
        => new AndSpecification(first, second);
    
    public static ISpecification Or(this ISpecification first, ISpecification second)
        => new OrSpecification(first, second);
}