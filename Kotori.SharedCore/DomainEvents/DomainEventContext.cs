namespace Kotori.SharedCore.DomainEvents;

public record DomainEventContext
{
    public required IUnitOfWork UnitOfWork { get; init; }
    public required CancellationToken CancellationToken { get; init; }
}