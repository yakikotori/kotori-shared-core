# Kotori.SharedCore

Personal libraries for building .NET projects that follow **Clean Architecture** and **Domain-Driven Design**.

Not designed to be used by others as is, but open-source to be shared if someone wants to see.

## Packages

| Package | Purpose |
| --- | --- |
| `Kotori.SharedCore` | Core abstractions: DDD building blocks, CQRS, Result, Outbox and Inbox contracts. |
| `Kotori.SharedCore.EntityFrameworkCore` | EF Core implementations, Outbox and Inbox storage and background workers, MassTransit bridge. |
| `Kotori.SharedCore.Endpoints` | Minimal API endpoint conventions with auto-registration. |
| `Kotori.SharedCore.Rebus` | Rebus bridge: outbox relay to a broker and consumer lanes that run `IIntegrationEventHandler<TEvent>`, optionally deduplicated by the inbox. |

## Installation

The packages are only available on GitHub NuGet registry.

```bash
dotnet add package Kotori.SharedCore
dotnet add package Kotori.SharedCore.EntityFrameworkCore
dotnet add package Kotori.SharedCore.Endpoints
dotnet add package Kotori.SharedCore.Rebus
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

The outbox guarantees integration events are published **at least once** relative to the database commit — no event is lost if the broker or process dies, but a crash between publish and status update can cause a redelivery, so consumers must be idempotent. An aggregate writes an `OutboxMessageEntity` inside the same transaction as its state change; a background processor picks it up and hands it to an `IIntegrationEventDispatcher` (Rebus, MassTransit, or in-process handlers).

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

### Shutdown

Once the processor's cancellation token is cancelled, it starts no new message from the batch. A message whose dispatch is cancelled by the shutdown stays `Pending` without using up an attempt; an `OperationCanceledException` thrown while the token is not cancelled, such as an HTTP timeout, is an ordinary failure. The batch is saved with `CancellationToken.None`, so a deploy doesn't relay the messages it already dispatched again.

## Rebus

`Kotori.SharedCore.Rebus` turns the outbox into a relay to a broker and runs the handlers in consumer lanes. Handlers keep implementing `IIntegrationEventHandler<TEvent>` and never see Rebus. The package depends only on `Rebus` and `Rebus.ServiceProvider`; the service chooses the transport (for example `Rebus.RabbitMq`) and the timeout store (for example `Rebus.PostgreSql`).

### Publish

Register a one-way bus as the host's default bus and the Rebus dispatcher as the outbox's `IIntegrationEventDispatcher`:

```csharp
builder.Services.AddOutbox(builder.Configuration);
builder.Services.AddRebus(rebus => rebus.Transport(t => t.UseRabbitMqAsOneWayClient(connectionString)));
builder.Services.AddRebusIntegrationEventDispatcher();
```

`RebusIntegrationEventDispatcher` publishes the event under its runtime type and sets the Rebus message id to `IntegrationEvent.Id`, so relaying the same row twice publishes the same message id. An `ICorrelatedEvent` also gets its correlation id. A publish failure propagates, so the outbox retries the row; with Rebus.RabbitMq publisher confirms are on by default, so a row is marked processed only after the broker accepted it.

### Consume in lanes

A lane is a Rebus endpoint with one input queue, its own concurrency, its own retry policy and only its own handlers. Each lane runs in its own container through `AddRebusService`, so an event that two lanes subscribe to is delivered once to each lane, and each lane runs only its own handlers.

```csharp
var notificationRetry = IntegrationEventRetryPolicy
    .Create(immediateAttempts: 3, baseDelay: TimeSpan.FromSeconds(30), maxDelay: TimeSpan.FromMinutes(5), giveUpAfter: TimeSpan.FromMinutes(30))
    .Unwrap();

builder.Host.AddIntegrationEventLane(
    rebus => rebus
        .Transport(t => t.UseRabbitMq(connectionString, "shop.notifications").Prefetch(1))
        .Timeouts(t => t.StoreInPostgres(databaseConnectionString, "RebusTimeouts"))
        .Options(o => { o.SetNumberOfWorkers(1); o.SetMaxParallelism(1); }),
    errorQueueName: "shop.error",
    notificationRetry,
    lane => lane
        .Handle<OrderShipped, SendShippingEmail>()
        .Handle<OrderCancelled, SendCancellationEmail>()
        .Forward<TimeProvider>()
        .Forward<IUnitOfWorkFactory>());
```

- `Handle<TEvent, THandler>()` registers the handler as a singleton in the lane's container, registers `IntegrationEventMessageHandler<TEvent>` for the event, and subscribes the lane to the event when its bus is created. Several handlers of one event run in registration order; a throwing handler fails the message, and the retry runs every handler again, so handlers must be idempotent.
- `Forward<TService>()` copies the host's registrations of a singleton type into the lane's container. Handlers can depend only on forwarded types, `ILogger<T>`, and what the lane registers itself. `ILoggerFactory` and `IHostApplicationLifetime` are always forwarded.
- `UseInbox()` forwards the host's `IInbox` into the lane, so the lane skips a message it already handled. See [Inbox](#inbox). Without it, the lane runs every copy it receives.
- When a lane's bus is created, before it subscribes and before it consumes any message, the lane resolves its `IInbox`, every `IIntegrationEventHandler<TEvent>` of each of its events and its `IntegrationEventRetryPolicy` from its own container. If one of them can't be resolved, for example because a dependency wasn't forwarded or a lane calls `UseInbox()` in a host without `AddInbox`, host startup fails with an `InvalidOperationException` that names the lane's input queue and the resolution error, and the event type when a handler failed. Handlers are singletons, so the instances built by this check are the ones that later handle messages; their constructors must not have side effects.
- The helper calls `RetryStrategy` itself. Don't call it again in the configure callback: Rebus rejects the second registration when the bus is created.
- Handlers receive the message context's cancellation token, which is cancelled when the lane stops.

### Retries

Rebus tries a message `ImmediateAttempts` times. After that, `IntegrationEventMessageHandler<TEvent>` handles `IFailed<TEvent>` through the lane's `IntegrationEventRetryPolicy`: it defers the whole transport message by `BaseDelay * 2^n`, capped at `MaxDelay`, where `n` is the number of earlier deferrals from Rebus's `rbs2-defer-count` header. Once the deferrals have waited `GiveUpAfter` in total, it dead-letters the message into the lane's error queue with the error description. A replayed dead letter keeps its defer count, so it gets its immediate attempts and is dead-lettered again if it still fails.

Deferral needs a timeout store (`.Timeouts(...)`) or a transport with native delayed delivery. Without one, the deferred copy fails with "A timeout manager has not been configured" when it arrives back in the queue, and the message is dead-lettered within seconds instead of being retried later.

`IntegrationEventRetryPolicy.Create` returns a `Result`: at least one immediate attempt, a positive base delay, a max delay no shorter than the base delay, and a non-negative give-up time. A zero give-up time dead-letters right after the immediate attempts.

## Inbox

Delivery from the outbox through a broker is at least once. A lane can receive the same message twice: the outbox relays a row again when the broker's publisher confirm was lost, and the broker redelivers a message when the consumer stopped after its handlers ran but before the ack. Both copies carry the same message id, `IntegrationEvent.Id`. The inbox records the message id and the consumer after the message was handled, and a lane that uses it skips a message it has already recorded.

### Enable it

Add the inbox entity to your `DbContext`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfiguration(new OutboxMessageEntityTypeConfiguration());
    modelBuilder.ApplyConfiguration(new InboxMessageEntityTypeConfiguration());
}
```

A service that creates its schema with manual migrations creates the same table:

```sql
create table inbox_messages ("MessageId" uuid not null, "Consumer" varchar(200) not null, "ProcessedOnUtc" timestamptz not null, constraint "PK_inbox_messages" primary key ("MessageId", "Consumer"));
create index "IX_inbox_messages_ProcessedOnUtc" on inbox_messages ("ProcessedOnUtc");
```

The constraint and index names are the ones EF Core generates, so a later EF migration doesn't see a difference.

Register the inbox in the host and turn it on in each lane that should deduplicate:

```csharp
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddInbox(builder.Configuration);

builder.Host.AddIntegrationEventLane(
    configure, errorQueueName, retryPolicy,
    lane => lane
        .Handle<OrderShipped, SendShippingEmail>()
        .Forward<IUnitOfWorkFactory>()
        .UseInbox());
```

`AddInbox` registers the inbox repository factory (`IRepositoryFactory<IInboxMessageRepository>` through `EfInboxMessageRepository`), `IInbox`, `InboxCleaner` and the hosted `BackgroundInboxCleaner`. Like `AddOutbox`, it needs the unit of work wiring and a registered `TimeProvider`.

### How a lane uses it

The consumer is the lane's input queue name, so every lane keeps its own record: an event handled by one lane still runs on another lane that subscribes to it. For each message, `IntegrationEventMessageHandler<TEvent>`:

1. asks `IInbox.HasProcessedAsync(message.Id, inputQueue)`, and if the message was recorded, logs it at debug level and acks it without running any handler;
2. runs every handler of the event;
3. calls `IInbox.MarkProcessedAsync(message.Id, inputQueue)`.

A handler that throws leaves the message unrecorded, so the retries and deferrals described above run every handler again. The record is written with `CancellationToken.None`, so a lane that stops after its handlers succeeded still records the message. Each call uses its own unit of work, separate from the handlers' work, so a handler is still at least once in the short window between its success and the record. `MarkProcessedAsync` is an `INSERT ... ON CONFLICT DO NOTHING`, so two copies handled at the same time don't fail each other.

A lane without `UseInbox()` gets an inbox that never reports a message as processed and records nothing, so it runs every copy it receives, as before 1.5.0.

### Configure

```json
{
  "InboxCleaner":           { "ClearProcessedAfter": "7.00:00:00" },
  "BackgroundInboxCleaner": { "DelayMs": 60000 }
}
```

Both sections are optional and default to the values above. `BackgroundInboxCleaner` deletes the records older than `ClearProcessedAfter`. Keep it longer than a message can stay in a queue, including deferrals and dead letters you may replay, because a copy that arrives after its record was deleted runs again.

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
| Messaging bridge | `MassTransitIntegrationEventDispatcher`, `RebusIntegrationEventDispatcher` |
| In-process dispatch | `ServiceProviderIntegrationEventDispatcher`, `IIntegrationEventHandler<TEvent>`, `ServiceCollectionExtensions.AddIntegrationEventHandler` |

### Transactional Inbox

| Pattern | Key types |
| --- | --- |
| Inbox Message | `IInboxMessage`, `InboxMessageEntity`, `InboxMessageEntityTypeConfiguration` |
| Storage | `IInboxMessageRepository`, `EfInboxMessageRepository` |
| Inbox | `IInbox`, `Inbox`, `ServiceCollectionExtensions.AddInbox` |
| Cleaner | `IInboxCleaner`, `InboxCleaner`, `BackgroundInboxCleaner` |

### Rebus

| Pattern | Key types |
| --- | --- |
| Publisher | `RebusIntegrationEventDispatcher`, `ServiceCollectionExtensions.AddRebusIntegrationEventDispatcher` |
| Consumer lane | `HostBuilderExtensions.AddIntegrationEventLane`, `IntegrationEventLane`, `IntegrationEventMessageHandler<TEvent>` |
| Deduplication | `IntegrationEventLane.UseInbox`, `IntegrationEventLaneConsumer` |
| Retry policy | `IntegrationEventRetryPolicy`, `IntegrationEventRetryDecision`, `DeferIntegrationEvent`, `DeadLetterIntegrationEvent` |

### Endpoints

| Pattern | Key types |
| --- | --- |
| Minimal API Endpoint | `IEndpoint`, `ServiceCollectionExtensions.AddEndpoints`, `WebApplicationExtensions.MapEndpoints` |

## License

See [LICENSE](LICENSE).