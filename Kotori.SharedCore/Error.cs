using System.Net;

namespace Kotori.SharedCore;

public abstract record Error;

public record ManyError<T>(IReadOnlyCollection<T> Errors) : Error where T : Error
{
    public override string ToString()
        => string.Join(Environment.NewLine, Errors.Select(error => error.ToString()));
}

public record TextError(string Message) : Error
{
    public static implicit operator TextError(string message)
        => new(message);

    public override string ToString()
        => Message;
}

public record HttpError(HttpStatusCode StatusCode) : Error
{
    public static implicit operator HttpError(HttpStatusCode statusCode)
        => new(statusCode);

    public override string ToString()
        => $"{StatusCode}";
}