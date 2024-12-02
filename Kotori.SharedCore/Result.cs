namespace Kotori.SharedCore;

public abstract record Result<TError> where TError : Error
{
    public bool IsOk => this is Ok<TError>;
    
    public static Ok<TError> Ok()
        => new();
        
    public static Fail<TError> Fail(TError error)
        => new (error);
    
    public static implicit operator Result<TError>(TError error)
        => new Fail<TError>(error);
}

public record Ok<TError> : Result<TError> where TError : Error;

public record Fail<TError>(TError Error) : Result<TError> where TError : Error;

public abstract record Result<TData, TError> where TError : Error
{
    public static Ok<TData, TError> Ok(TData data)
        => new(data);
    
    public static Fail<TData, TError> Fail(TError error)
        => new (error);
    
    public static implicit operator Result<TData, TError>(TData data)
        => new Ok<TData, TError>(data);
    
    public static implicit operator Result<TData, TError>(TError error)
        => new Fail<TData, TError>(error);
}

public record Ok<TData, TError>(TData Data) : Result<TData, TError> where TError : Error;

public record Fail<TData, TError>(TError Error) : Result<TData, TError> where TError : Error;