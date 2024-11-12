namespace Kotori.SharedCore.Exceptions;

public class DomainEventHandlerNotFoundException : Exception
{
    public Type DomainEventHandlerType { get; }

    public DomainEventHandlerNotFoundException(Type domainEventHandlerType)
    {
        DomainEventHandlerType = domainEventHandlerType;
    }

    public override string ToString()
        => $"Domain event handler of type <{DomainEventHandlerType}> was not found.{Environment.NewLine}{base.ToString()}";
}