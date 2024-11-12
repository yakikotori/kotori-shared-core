namespace Kotori.SharedCore.UseCases.Exceptions;

public class CommandNotFoundException : Exception
{
    public Type CommandType { get; }
    public Type ResultType { get; }

    public CommandNotFoundException(Type commandType, Type resultType)
    {
        CommandType = commandType;
        ResultType = resultType;
    }

    public override string ToString()
        => $"Command of type <{CommandType}, {ResultType}> was not found.{Environment.NewLine}{base.ToString()}";
}