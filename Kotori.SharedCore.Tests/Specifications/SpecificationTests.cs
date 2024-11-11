using FluentAssertions;
using Kotori.SharedCore.Specifications;

namespace Kotori.SharedCore.Tests.Specifications;

public class SpecificationTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    public void AndSpecificationWorks(bool firstValue, bool secondValue, bool isSatisfied)
    {
        var first = new TestSpecification(firstValue);
        var second = new TestSpecification(secondValue);
        
        first
            .And(second)
            .IsSatisfied()
            .Should()
            .Be(isSatisfied);
    }
    
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    public void OrSpecificationWorks(bool firstValue, bool secondValue, bool isSatisfied)
    {
        var first = new TestSpecification(firstValue);
        var second = new TestSpecification(secondValue);
        
        first
            .Or(second)
            .IsSatisfied()
            .Should()
            .Be(isSatisfied);
    }
}