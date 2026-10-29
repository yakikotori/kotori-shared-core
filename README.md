# Kotori.SharedCore

Personal libraries for building .NET projects that follow **Clean Architecture** and **Domain-Driven Design**.

Not designed to be used by others as is, but open-source to be shared if someone wants to see.

## Packages

| Package | Purpose |
| --- | --- |
| `Kotori.SharedCore` | Core abstractions: DDD building blocks, CQRS, Result, Outbox contracts. |
| `Kotori.SharedCore.EntityFrameworkCore` | EF Core implementations, Outbox storage and background workers, MassTransit bridge. |
| `Kotori.SharedCore.Endpoints` | Minimal API endpoint conventions with auto-registration. |

## Installation

The packages are only available on GitHub NuGet registry.

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

## Domain events

Domain events registered via `EntityBase.RegisterDomainEvent` are dispatched **synchronously, inside the same transaction** as the aggregate change. `EfUnitOfWork.SaveChangesAsync` collects events from tracked entities, runs every `IDomainEventHandler<T>` (resolved through DI via `ServiceProviderDomainEventDispatcher`), then calls `DbContext.SaveChangesAsync` and commits. A handler that writes to the same `DbContext` is atomic with the originating change — if any handler throws, the whole unit of work rolls back.

This is the right tool for **in-process, same-database** side effects: updating a read model, adjusting another aggregate in the same bounded context, enforcing a cross-aggregate invariant.

**Don't call external systems from a domain event handler.** HTTP calls, message-broker publishes, email, file I/O, and anything else outside the DB transaction are not rolled back if the commit fails, which reintroduces the dual-write problem. For those, write an `IIntegrationEvent` to the **outbox** inside the same unit of work — the `BackgroundOutboxProcessor` will dispatch it after the commit lands.

| Use case | Use |
| --- | --- |
| Update a read model / denormalized table | Domain event handler |
| Keep another aggregate consistent | Domain event handler |
| Publish to a message broker | Outbox → `IIntegrationEvent` |
| Call a third-party API | Outbox → `IIntegrationEvent` (handler on the consumer side) |
| Send email / notification | Outbox → `IIntegrationEvent` |

## Transactional Outbox

The outbox guarantees integration events are published **at least once** relative to the database commit — no event is lost if the broker or process dies, but a crash between publish and status update can cause a redelivery, so consumers must be idempotent. An aggregate writes an `OutboxMessageEntity` inside the same transaction as its state change; a background processor picks it up and hands it to an `IIntegrationEventDispatcher` (MassTransit, or in-process handlers).

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
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOutbox(builder.Configuration);
builder.Services.AddSingleton<IIntegrationEventDispatcher, MassTransitIntegrationEventDispatcher>();
```

`AddOutbox` does not register `TimeProvider`; the outbox repository and processor require it.

### Dispatch in process

To handle integration events inside the same service instead of publishing to a broker, use `ServiceProviderIntegrationEventDispatcher` and implement `IIntegrationEventHandler<TEvent>`:

```csharp
public class SendShippingEmail : IIntegrationEventHandler<OrderShipped>
{
    public Task HandleAsync(OrderShipped integrationEvent, CancellationToken cancellationToken) => ...;
}

builder.Services.AddOutbox(builder.Configuration);
builder.Services.AddSingleton<IIntegrationEventDispatcher, ServiceProviderIntegrationEventDispatcher>();
builder.Services.AddIntegrationEventHandler<OrderShipped, SendShippingEmail>();
```

The dispatcher resolves every `IIntegrationEventHandler<TEvent>` registered for the event's runtime type and awaits them in registration order. `AddIntegrationEventHandler` registers the handler as a singleton and ignores a duplicate registration of the same handler. A handler that throws stops the remaining handlers for that message and sends it through the retry policy below, so every handler must be idempotent: on retry, the handlers that already succeeded run again.

Unlike `ServiceProviderDomainEventDispatcher`, which does nothing when an event has no handler, `ServiceProviderIntegrationEventDispatcher` throws `InvalidOperationException` when no handler is registered. The message is retried and eventually marked `Failed` instead of being marked `Processed` and silently dropped.

### Configure

```json
{
  "OutboxProcessor":           { "BatchSize": 100, "MaxAttempts": 10, "RetryBaseDelay": "00:00:05", "RetryMaxDelay": "00:10:00" },
  "BackgroundOutboxProcessor": { "DelayMs": 500 },
  "OutboxCleaner":             { "ClearProcessedAfter": "7.00:00:00", "ClearFailedAfter": "30.00:00:00" },
  "BackgroundOutboxCleaner":   { "DelayMs": 60000 }
}
```

`OutboxProcessor:BatchSize` is required. Every other value falls back to the default shown above when unbound, so the `OutboxCleaner` section can be omitted: processed messages are kept for 7 days and failed messages for 30 days. A configured value overrides its default.

`BatchSize` has no default because the right value depends on the dispatcher: the whole batch is dispatched inside one transaction that holds row locks, so slow in-process handlers call for a smaller batch than a broker publish. The configuration binder does not enforce `required`; a missing `BatchSize` binds as `0` and the processor picks up nothing.

### How messages get written

Inside a use case, serialize the integration event and persist it through the outbox repository in the same unit of work as the aggregate change. `BackgroundOutboxProcessor` polls `IOutboxMessageRepository.GetPendingAsync` for `Pending` messages whose `NextAttemptOnUtc` has passed, dispatches each message, and marks it `Processed`. `BackgroundOutboxCleaner` purges old rows based on `ClearProcessedAfter` / `ClearFailedAfter`.

### Retries

When dispatch throws, the message stays `Pending`, `Attempts` is incremented, the error is stored in `ErrorMessage`, and `NextAttemptOnUtc` is pushed out by `RetryBaseDelay * 2^(attempt - 1)`, capped at `RetryMaxDelay`. The attempt that reaches `MaxAttempts` marks the message `Failed`. A message whose payload cannot be deserialized is marked `Failed` immediately, since retrying cannot fix it.

## Patterns

### Domain-Driven Design

| Pattern | Key types |
| --- | --- |
| Aggregate Root | `IAggregateRoot` |
| Entity | `EntityBase` — tracks pending domain events |
| Value Object | `IValueObject` |
| Domain Events | `IDomainEvent`, `IDomainEventHandler<T>`, `IDomainEventDispatcher`, `ServiceProviderDomainEventDispatcher` |
| Integration Events | `IIntegrationEvent`, `IntegrationEvent`, `ICorrelatedEvent`, `IIntegrationEventHandler<TEvent>`, `IIntegrationEventDispatcher`, `ServiceProviderIntegrationEventDispatcher` |
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
| In-process dispatch | `ServiceProviderIntegrationEventDispatcher`, `IIntegrationEventHandler<TEvent>`, `ServiceCollectionExtensions.AddIntegrationEventHandler` |

### Endpoints

| Pattern | Key types |
| --- | --- |
| Minimal API Endpoint | `IEndpoint`, `ServiceCollectionExtensions.AddEndpoints`, `WebApplicationExtensions.MapEndpoints` |

## License

See [LICENSE](LICENSE).