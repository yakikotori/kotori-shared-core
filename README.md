# Kotori.SharedCore

Libraries for building .NET projects that follow **Clean Architecture** and **Domain-Driven Design**.

Targets `net9.0` and `net10.0`.

## Packages

| Package | Purpose |
| --- | --- |
| `Kotori.SharedCore` | Core abstractions: DDD building blocks, CQRS, Result, Outbox contracts. |
| `Kotori.SharedCore.EntityFrameworkCore` | EF Core implementations, Outbox storage and background workers, MassTransit bridge. |
| `Kotori.SharedCore.Endpoints` | Minimal API endpoint conventions with auto-registration. |

## Installation

```bash
dotnet add package Kotori.SharedCore
dotnet add package Kotori.SharedCore.EntityFrameworkCore
dotnet add package Kotori.SharedCore.Endpoints
```

## Quick start

### 1. Define a command, an error, and a handler

Errors are plain records. Handlers return `Result<TData, TError>`; implicit conversions let you `return data;` or `return error;` directly.

```csharp
public record CreateOrderCommand(Guid CustomerId) : ICommand;

public abstract record OrderError : Error;
public sealed record CustomerNotFound(Guid CustomerId) : OrderError;

public class CreateOrderHandler : ICommandHandler<CreateOrderCommand, Result<Guid, OrderError>>
{
    private readonly IUnitOfWorkFactory _uowFactory;

    public CreateOrderHandler(IUnitOfWorkFactory uowFactory) => _uowFactory = uowFactory;

    public async Task<Result<Guid, OrderError>> ExecuteAsync(CreateOrderCommand command, CancellationToken ct = default)
    {
        await using var uow = await _uowFactory.CreateAsync();

        var customers = uow.GetRepository<ICustomerRepository>();
        if (!await customers.ExistsAsync(command.CustomerId, ct))
            return new CustomerNotFound(command.CustomerId);

        var orders = uow.GetRepository<IOrderRepository>();
        var order = Order.Create(command.CustomerId);

        await orders.AddAsync(order, ct);
        await uow.SaveChangesAsync(ct);

        return order.Id;
    }
}
```

### 2. Wire up DI

```csharp
builder.Services.AddDbContextFactory<AppDbContext>(opt => opt.UseNpgsql(connStr));

builder.Services.AddSingleton<IDomainEventDispatcher, ServiceProviderDomainEventDispatcher>();
builder.Services.AddSingleton<IUnitOfWorkFactory, EfUnitOfWorkFactory<AppDbContext>>();
builder.Services.AddSingleton<IUseCaseBus, ServiceProviderUseCaseBus>();

builder.Services.AddSingleton<ICommandHandler<CreateOrderCommand, Result<Guid, OrderError>>, CreateOrderHandler>();
builder.Services.AddSingleton<IRepositoryFactory<IOrderRepository>, EfRepositoryFactory<EfOrderRepository>>();
```

### 3. Dispatch and pattern-match the result

```csharp
var result = await useCaseBus.ExecuteCommandAsync<CreateOrderCommand, Result<Guid, OrderError>>(
    new CreateOrderCommand(customerId), ct);

return result switch
{
    Ok<Guid, OrderError> ok                              => Results.Ok(ok.Data),
    Fail<Guid, OrderError>(CustomerNotFound e)           => Results.NotFound(e),
    Fail<Guid, OrderError> fail                          => Results.Problem(fail.Error.ToString())
};
```

### 4. Minimal API endpoints

Implement `IEndpoint`:

```csharp
public class CreateOrderEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder routes) =>
        routes.MapPost("/orders", async (CreateOrderCommand cmd, IUseCaseBus bus, CancellationToken ct) =>
        {
            var result = await bus.ExecuteCommandAsync<CreateOrderCommand, Result<Guid, OrderError>>(cmd, ct);

            return result switch
            {
                Ok<Guid, OrderError> ok                    => Results.Ok(ok.Data),
                Fail<Guid, OrderError>(CustomerNotFound e) => Results.NotFound(e),
                Fail<Guid, OrderError> fail                => Results.Problem(fail.Error.ToString())
            };
        });
}
```

Then register and map:

```csharp
builder.Services.AddEndpoints(typeof(Program).Assembly);
app.MapEndpoints();
```

## Transactional Outbox

The outbox guarantees integration events are published **exactly once** relative to the database commit. An aggregate writes an `OutboxMessageEntity` inside the same transaction as its state change; a background processor picks it up and hands it to an `IIntegrationEventDispatcher` (e.g. MassTransit).

### Enable it

Add the outbox entity to your `DbContext`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfiguration(new OutboxMessageEntityTypeConfiguration());
}
```

Register services and publisher:

```csharp
builder.Services.AddOutbox(builder.Configuration);
builder.Services.AddSingleton<IIntegrationEventDispatcher, MassTransitIntegrationEventDispatcher>();
```

### Configure

```json
{
  "OutboxProcessor":           { "BatchSize": 100 },
  "BackgroundOutboxProcessor": { "DelayMs": 500 },
  "OutboxCleaner":             { "ClearProcessedAfter": "1.00:00:00", "ClearFailedAfter": "7.00:00:00" },
  "BackgroundOutboxCleaner":   { "DelayMs": 60000 }
}
```

### How messages get written

Inside a use case, serialize the integration event and persist it through the outbox repository in the same unit of work as the aggregate change. `BackgroundOutboxProcessor` polls `IOutboxMessageRepository.GetPendingAsync`, dispatches each message, and marks it `Processed` or `Failed`. `BackgroundOutboxCleaner` purges old rows based on `ClearProcessedAfter` / `ClearFailedAfter`.

## Patterns

### Domain-Driven Design

| Pattern | Key types |
| --- | --- |
| Aggregate Root | `IAggregateRoot` |
| Entity | `EntityBase` — tracks pending domain events |
| Value Object | `IValueObject` |
| Domain Events | `IDomainEvent`, `IDomainEventHandler<T>`, `IDomainEventDispatcher`, `ServiceProviderDomainEventDispatcher` |
| Integration Events | `IIntegrationEvent`, `IntegrationEvent`, `ICorrelatedEvent`, `IIntegrationEventDispatcher` |
| Specification | `ISpecification<T>`, `AndSpecification`, `OrSpecification`, `SpecificationExtensions` |

### Persistence

| Pattern | Key types |
| --- | --- |
| Repository | `IRepository<T>` |
| Unit of Work | `IUnitOfWork`, `IUnitOfWorkFactory` |
| EF Unit of Work | `EfUnitOfWork<TContext>`, `EfUnitOfWorkFactory` — dispatches domain events on save |
| Repository Factory | `IRepositoryFactory<T>`, `EfRepositoryFactory` |

### CQRS

| Pattern | Key types |
| --- | --- |
| Command / Query | `ICommand`, `IQuery` |
| Handlers | `ICommandHandler<TCommand, TResult>`, `IQueryHandler<TQuery, TResponse>` |
| Mediator / Use Case Bus | `IUseCaseBus`, `ServiceProviderUseCaseBus` |

### Result & Errors

| Pattern | Key types |
| --- | --- |
| Result | `Result<TError>`, `Result<TData, TError>` |
| Errors | `Error` hierarchy |

### Transactional Outbox

| Pattern | Key types |
| --- | --- |
| Outbox Message | `IOutboxMessage`, `OutboxMessageEntity`, `OutboxMessageState` |
| Storage | `IOutboxMessageRepository`, `EfOutboxMessageRepository` |
| Serialization | `IEventSerializer`, `JsonEventSerializer` |
| Processor | `IOutboxProcessor`, `OutboxProcessor`, `BackgroundOutboxProcessor` |
| Cleaner | `IOutboxCleaner`, `OutboxCleaner`, `BackgroundOutboxCleaner` |
| Messaging bridge | `MassTransitIntegrationEventDispatcher` |

### Endpoints

| Pattern | Key types |
| --- | --- |
| Minimal API Endpoint | `IEndpoint`, `ServiceCollectionExtensions.AddEndpoints`, `WebApplicationExtensions.MapEndpoints` |

## License

See [LICENSE](LICENSE).