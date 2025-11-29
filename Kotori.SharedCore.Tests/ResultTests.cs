namespace Kotori.SharedCore.Tests;

public class ResultTests
{
    private Result<int, TextError> DoSomething()
    {
        var success = false;

        if (!success)
        {
            return new TextError("Ошибка потому-что что-то не так");
        }

        return 123;
    }
    
    public void ItWorks()
    {
        var result = DoSomething();

        if (result is Fail<int, TextError> fail)
        {
            Console.WriteLine($"Error: {fail.Error.Message}");
            
            return;
        }
        
        var data = result.Unwrap();
        
        Console.WriteLine($"Data: {data}");
    }
}