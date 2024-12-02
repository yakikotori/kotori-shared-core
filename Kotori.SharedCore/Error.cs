namespace Kotori.SharedCore;

public abstract record Error;

public record ManyError<T>(IReadOnlyCollection<T> Errors) : Error where T : Error;

public record TextError(string Message) : Error
{
    public static implicit operator TextError(string message)
        => new(message);
}