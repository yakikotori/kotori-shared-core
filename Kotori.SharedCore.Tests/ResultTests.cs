namespace Kotori.SharedCore.Tests;

public class ResultTests
{
    private Result<TextError> DoSomething()
    {
        return Result<TextError>.Fail("Cats are too hungry to work");
    }
    
    [Fact]
    public void ItWorks()
    {
        var result = DoSomething();
    }
}