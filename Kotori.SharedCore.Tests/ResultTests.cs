namespace Kotori.SharedCore.Tests;

public class ResultTests
{
    private Result<string, TextError> DoSomething()
    {
        return Result<string, TextError>.Fail("Cats are too hungry to work");
    }
    
    [Fact]
    public void ItWorks()
    {
    }
}