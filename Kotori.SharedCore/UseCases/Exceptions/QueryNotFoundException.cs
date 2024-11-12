namespace Kotori.SharedCore.UseCases.Exceptions;

public class QueryNotFoundException : Exception
{
    public Type QueryType { get; }
    public Type ResponseType { get; }

    public QueryNotFoundException(Type queryType, Type responseType)
    {
        QueryType = queryType;
        ResponseType = responseType;
    }

    public override string ToString()
        => $"Query of type <{QueryType}, {ResponseType}> was not found.{Environment.NewLine}{base.ToString()}";
}