namespace Kotori.SharedCore;

public record Result<TData, TError> where TError : Error
{
    public static Ok<TData, TError> Ok(TData data)
        => new(data);
    
    public static Fail<TData, TError> Fail(TError error)
        => new (error);
}

public record Ok<TData, TError>(TData Data) : Result<TData, TError> where TError : Error;

public record Fail<TData, TError>(TError Error) : Result<TData, TError> where TError : Error;