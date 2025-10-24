namespace Kotori.SharedCore.Tests;

public class ResultTests
{
    private Result<string, TextError> DoSomething()
    {
        return new TextError("Cats are too hungry to work");
    }
    
    [Fact]
    public void ItWorks()
    {
        var result = DoSomething();

        var data = result.Unwrap();
    }
}