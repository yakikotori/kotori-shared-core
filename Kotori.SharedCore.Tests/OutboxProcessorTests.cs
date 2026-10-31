using FluentAssertions;
using Kotori.SharedCore.EntityFrameworkCore.Outbox;
using Kotori.SharedCore.IntegrationEvents;
using Kotori.SharedCore.Outbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Kotori.SharedCore.Tests;

public class OutboxProcessorTests
{
    [Fact]
    public async Task OutboxProcessor_MarksAsFailed_WhenFailedToDeserializeEvent()
    {
        var logger = Substitute.For<ILogger<OutboxProcessor>>();
        var options = Substitute.For<IOptions<OutboxProcessorOptions>>();
        var uowFactory = Substitute.For<IUnitOfWorkFactory>();
        var eventSerializer = Substitute.For<IEventSerializer>();
        var integrationEventDispatcher = Substitute.For<IIntegrationEventDispatcher>();
        var timeProvider = Substitute.For<TimeProvider>();

        options.Value.Returns(new OutboxProcessorOptions { BatchSize = 1 });
        
        var uow = Substitute.For<IUnitOfWork>();

        uowFactory.CreateAsync().Returns(uow);

        uow.BeginTransactionAsync().Returns(Task.CompletedTask);
        
        var outboxMessageRepository = Substitute.For<IOutboxMessageRepository>();

        uow.GetRepository<IOutboxMessageRepository>().Returns(outboxMessageRepository);

        List<IOutboxMessage> outboxMessages = [
            OutboxMessageEntity.Create("Bad", "{}", DateTime.UtcNow)
        ];

        outboxMessageRepository.GetPendingAsync(Arg.Any<int>()).ReturnsForAnyArgs(outboxMessages);

        eventSerializer.Deserialize("", "").ReturnsForAnyArgs(new TextError("Fail"));
        
        var outboxProcessor = new OutboxProcessor(logger, options, uowFactory, eventSerializer,
            integrationEventDispatcher, timeProvider, Substitute.For<IOutboxMetrics>());

        await outboxProcessor.ProcessAsync();
        
        foreach (var outboxMessage in outboxMessages)
        {
            outboxMessage.State.Should().Be(OutboxMessageState.Failed);
        }
    }

    [Fact]
    public async Task OutboxProcessor_MarksAsProcessed_WhenEventDispatchedSuccessfully()
    {
        var logger = Substitute.For<ILogger<OutboxProcessor>>();
        var options = Substitute.For<IOptions<OutboxProcessorOptions>>();
        var uowFactory = Substitute.For<IUnitOfWorkFactory>();
        var eventSerializer = Substitute.For<IEventSerializer>();
        var integrationEventDispatcher = Substitute.For<IIntegrationEventDispatcher>();
        var timeProvider = Substitute.For<TimeProvider>();

        options.Value.Returns(new OutboxProcessorOptions { BatchSize = 1 });

        var uow = Substitute.For<IUnitOfWork>();
        uowFactory.CreateAsync().Returns(uow);
        uow.BeginTransactionAsync().Returns(Task.CompletedTask);

        var outboxMessageRepository = Substitute.For<IOutboxMessageRepository>();
        uow.GetRepository<IOutboxMessageRepository>().Returns(outboxMessageRepository);

        List<IOutboxMessage> outboxMessages =
        [
            OutboxMessageEntity.Create("TestEvent", "{}", DateTime.UtcNow)
        ];

        outboxMessageRepository.GetPendingAsync(Arg.Any<int>()).Returns(outboxMessages);

        var mockEvent = Substitute.For<IIntegrationEvent>();
        eventSerializer.Deserialize(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new Ok<IIntegrationEvent, TextError>(mockEvent));

        var outboxProcessor = new OutboxProcessor(logger, options, uowFactory, eventSerializer,
            integrationEventDispatcher, timeProvider, Substitute.For<IOutboxMetrics>());

        await outboxProcessor.ProcessAsync();

        foreach (var outboxMessage in outboxMessages)
        {
            outboxMessage.State.Should().Be(OutboxMessageState.Processed);
        }

        await integrationEventDispatcher.Received(1).DispatchAsync(mockEvent, Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OutboxProcessor_MarksAsFailed_WhenDispatcherThrowsExceptionOnLastAttempt()
    {
        var logger = Substitute.For<ILogger<OutboxProcessor>>();
        var options = Substitute.For<IOptions<OutboxProcessorOptions>>();
        var uowFactory = Substitute.For<IUnitOfWorkFactory>();
        var eventSerializer = Substitute.For<IEventSerializer>();
        var integrationEventDispatcher = Substitute.For<IIntegrationEventDispatcher>();
        var timeProvider = Substitute.For<TimeProvider>();

        options.Value.Returns(new OutboxProcessorOptions { BatchSize = 1, MaxAttempts = 1 });

        var uow = Substitute.For<IUnitOfWork>();
        uowFactory.CreateAsync().Returns(uow);
        uow.BeginTransactionAsync().Returns(Task.CompletedTask);

        var outboxMessageRepository = Substitute.For<IOutboxMessageRepository>();
        uow.GetRepository<IOutboxMessageRepository>().Returns(outboxMessageRepository);

        List<IOutboxMessage> outboxMessages =
        [
            OutboxMessageEntity.Create("TestEvent", "{}", DateTime.UtcNow)
        ];

        outboxMessageRepository.GetPendingAsync(Arg.Any<int>()).Returns(outboxMessages);

        var mockEvent = Substitute.For<IIntegrationEvent>();
        eventSerializer.Deserialize(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new Ok<IIntegrationEvent, TextError>(mockEvent));

        integrationEventDispatcher
            .DispatchAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Dispatch failed")));

        var outboxProcessor = new OutboxProcessor(logger, options, uowFactory, eventSerializer,
            integrationEventDispatcher, timeProvider, Substitute.For<IOutboxMetrics>());

        await outboxProcessor.ProcessAsync();

        foreach (var outboxMessage in outboxMessages)
        {
            outboxMessage.State.Should().Be(OutboxMessageState.Failed);
        }
    }

    [Fact]
    public async Task OutboxProcessor_ProcessesMultipleMessages_WithMixedOutcomes()
    {
        var logger = Substitute.For<ILogger<OutboxProcessor>>();
        var options = Substitute.For<IOptions<OutboxProcessorOptions>>();
        var uowFactory = Substitute.For<IUnitOfWorkFactory>();
        var eventSerializer = Substitute.For<IEventSerializer>();
        var integrationEventDispatcher = Substitute.For<IIntegrationEventDispatcher>();
        var timeProvider = Substitute.For<TimeProvider>();

        options.Value.Returns(new OutboxProcessorOptions { BatchSize = 3 });

        var uow = Substitute.For<IUnitOfWork>();
        uowFactory.CreateAsync().Returns(uow);
        uow.BeginTransactionAsync().Returns(Task.CompletedTask);

        var outboxMessageRepository = Substitute.For<IOutboxMessageRepository>();
        uow.GetRepository<IOutboxMessageRepository>().Returns(outboxMessageRepository);

        var successMessage = OutboxMessageEntity.Create("SuccessEvent", "{}", DateTime.UtcNow);
        var failDeserializeMessage = OutboxMessageEntity.Create("BadEvent", "{}", DateTime.UtcNow);
        var failDispatchMessage = OutboxMessageEntity.Create("DispatchFailEvent", "{}", DateTime.UtcNow);

        List<IOutboxMessage> outboxMessages = [successMessage, failDeserializeMessage, failDispatchMessage];

        outboxMessageRepository.GetPendingAsync(Arg.Any<int>()).Returns(outboxMessages);

        var successEvent = Substitute.For<IIntegrationEvent>();
        var dispatchFailEvent = Substitute.For<IIntegrationEvent>();

        eventSerializer.Deserialize("SuccessEvent", Arg.Any<string>())
            .Returns(new Ok<IIntegrationEvent, TextError>(successEvent));
        eventSerializer.Deserialize("BadEvent", Arg.Any<string>())
            .Returns(new TextError("Deserialization failed"));
        eventSerializer.Deserialize("DispatchFailEvent", Arg.Any<string>())
            .Returns(new Ok<IIntegrationEvent, TextError>(dispatchFailEvent));

        integrationEventDispatcher.DispatchAsync(successEvent, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        integrationEventDispatcher.DispatchAsync(dispatchFailEvent, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Dispatch failed")));

        var outboxProcessor = new OutboxProcessor(logger, options, uowFactory, eventSerializer,
            integrationEventDispatcher, timeProvider, Substitute.For<IOutboxMetrics>());

        await outboxProcessor.ProcessAsync();

        successMessage.State.Should().Be(OutboxMessageState.Processed);
        failDeserializeMessage.State.Should().Be(OutboxMessageState.Failed);
        failDispatchMessage.State.Should().Be(OutboxMessageState.Pending);
        failDispatchMessage.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task OutboxProcessor_SchedulesRetry_WhenDispatcherThrowsExceptionBeforeLastAttempt()
    {
        var logger = Substitute.For<ILogger<OutboxProcessor>>();
        var options = Substitute.For<IOptions<OutboxProcessorOptions>>();
        var uowFactory = Substitute.For<IUnitOfWorkFactory>();
        var eventSerializer = Substitute.For<IEventSerializer>();
        var integrationEventDispatcher = Substitute.For<IIntegrationEventDispatcher>();
        var timeProvider = Substitute.For<TimeProvider>();

        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        timeProvider.GetUtcNow().Returns(now);

        options.Value.Returns(new OutboxProcessorOptions
        {
            BatchSize = 1,
            MaxAttempts = 3,
            RetryBaseDelay = TimeSpan.FromSeconds(10),
            RetryMaxDelay = TimeSpan.FromMinutes(1)
        });

        var uow = Substitute.For<IUnitOfWork>();
        uowFactory.CreateAsync().Returns(uow);
        uow.BeginTransactionAsync().Returns(Task.CompletedTask);

        var outboxMessageRepository = Substitute.For<IOutboxMessageRepository>();
        uow.GetRepository<IOutboxMessageRepository>().Returns(outboxMessageRepository);

        var outboxMessage = OutboxMessageEntity.Create("TestEvent", "{}", now.UtcDateTime);

        outboxMessageRepository.GetPendingAsync(Arg.Any<int>()).Returns([outboxMessage]);

        var mockEvent = Substitute.For<IIntegrationEvent>();
        eventSerializer.Deserialize(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new Ok<IIntegrationEvent, TextError>(mockEvent));

        integrationEventDispatcher
            .DispatchAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Dispatch failed")));

        var outboxProcessor = new OutboxProcessor(logger, options, uowFactory, eventSerializer,
            integrationEventDispatcher, timeProvider, Substitute.For<IOutboxMetrics>());

        await outboxProcessor.ProcessAsync();

        outboxMessage.State.Should().Be(OutboxMessageState.Pending);
        outboxMessage.Attempts.Should().Be(1);
        outboxMessage.ErrorMessage.Should().Be("Dispatch failed");
        outboxMessage.ProcessedOnUtc.Should().BeNull();
        outboxMessage.NextAttemptOnUtc.Should().Be(now.UtcDateTime.AddSeconds(10));
    }

    [Fact]
    public async Task OutboxProcessor_DoublesRetryDelay_UpToMaxDelay()
    {
        var logger = Substitute.For<ILogger<OutboxProcessor>>();
        var options = Substitute.For<IOptions<OutboxProcessorOptions>>();
        var uowFactory = Substitute.For<IUnitOfWorkFactory>();
        var eventSerializer = Substitute.For<IEventSerializer>();
        var integrationEventDispatcher = Substitute.For<IIntegrationEventDispatcher>();
        var timeProvider = Substitute.For<TimeProvider>();

        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        timeProvider.GetUtcNow().Returns(now);

        options.Value.Returns(new OutboxProcessorOptions
        {
            BatchSize = 1,
            MaxAttempts = 10,
            RetryBaseDelay = TimeSpan.FromSeconds(10),
            RetryMaxDelay = TimeSpan.FromMinutes(1)
        });

        var uow = Substitute.For<IUnitOfWork>();
        uowFactory.CreateAsync().Returns(uow);
        uow.BeginTransactionAsync().Returns(Task.CompletedTask);

        var outboxMessageRepository = Substitute.For<IOutboxMessageRepository>();
        uow.GetRepository<IOutboxMessageRepository>().Returns(outboxMessageRepository);

        var outboxMessage = OutboxMessageEntity.Create("TestEvent", "{}", now.UtcDateTime);

        outboxMessageRepository.GetPendingAsync(Arg.Any<int>()).Returns([outboxMessage]);

        var mockEvent = Substitute.For<IIntegrationEvent>();
        eventSerializer.Deserialize(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new Ok<IIntegrationEvent, TextError>(mockEvent));

        integrationEventDispatcher
            .DispatchAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Dispatch failed")));

        var outboxProcessor = new OutboxProcessor(logger, options, uowFactory, eventSerializer,
            integrationEventDispatcher, timeProvider, Substitute.For<IOutboxMetrics>());

        var retryDelays = new List<TimeSpan>();

        for (var i = 0; i < 5; i++)
        {
            await outboxProcessor.ProcessAsync();
            retryDelays.Add(outboxMessage.NextAttemptOnUtc - now.UtcDateTime);
        }

        retryDelays.Should().Equal(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(40),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));
        outboxMessage.Attempts.Should().Be(5);
        outboxMessage.State.Should().Be(OutboxMessageState.Pending);
    }

    [Fact]
    public async Task OutboxProcessor_StartsNoNewMessage_OnceShutdownIsRequested()
    {
        using var shutdown = new CancellationTokenSource();
        var first = OutboxMessageEntity.Create("First", "{}", DateTime.UtcNow);
        var second = OutboxMessageEntity.Create("Second", "{}", DateTime.UtcNow);
        var firstEvent = Substitute.For<IIntegrationEvent>();
        var secondEvent = Substitute.For<IIntegrationEvent>();
        var batch = CreateBatch([first, second]);

        batch.EventSerializer.Deserialize("First", Arg.Any<string>())
            .Returns(new Ok<IIntegrationEvent, TextError>(firstEvent));
        batch.EventSerializer.Deserialize("Second", Arg.Any<string>())
            .Returns(new Ok<IIntegrationEvent, TextError>(secondEvent));
        batch.Dispatcher.DispatchAsync(firstEvent, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                shutdown.Cancel();
                return Task.CompletedTask;
            });

        await batch.Processor.ProcessAsync(shutdown.Token);

        first.State.Should().Be(OutboxMessageState.Processed);
        second.State.Should().Be(OutboxMessageState.Pending);
        second.Attempts.Should().Be(0);
        await batch.Dispatcher.DidNotReceive().DispatchAsync(secondEvent, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OutboxProcessor_KeepsMessagePendingWithoutUsingAnAttempt_WhenShutdownCancelsItsDispatch()
    {
        using var shutdown = new CancellationTokenSource();
        var message = OutboxMessageEntity.Create("TestEvent", "{}", DateTime.UtcNow);
        var batch = CreateBatch([message]);

        batch.EventSerializer.Deserialize(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new Ok<IIntegrationEvent, TextError>(Substitute.For<IIntegrationEvent>()));
        batch.Dispatcher.DispatchAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                shutdown.Cancel();
                return Task.FromCanceled(call.Arg<CancellationToken>());
            });

        await batch.Processor.ProcessAsync(shutdown.Token);

        message.State.Should().Be(OutboxMessageState.Pending);
        message.Attempts.Should().Be(0);
        message.ErrorMessage.Should().BeNull();
        message.NextAttemptOnUtc.Should().Be(message.OccurredOnUtc);
    }

    [Fact]
    public async Task OutboxProcessor_CountsAnAttempt_WhenDispatchIsCancelledWithoutShutdown()
    {
        var message = OutboxMessageEntity.Create("TestEvent", "{}", DateTime.UtcNow);
        var batch = CreateBatch([message]);

        batch.EventSerializer.Deserialize(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new Ok<IIntegrationEvent, TextError>(Substitute.For<IIntegrationEvent>()));
        batch.Dispatcher.DispatchAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new TaskCanceledException("Request timed out")));

        await batch.Processor.ProcessAsync(CancellationToken.None);

        message.State.Should().Be(OutboxMessageState.Pending);
        message.Attempts.Should().Be(1);
        message.ErrorMessage.Should().Be("Request timed out");
    }

    [Fact]
    public async Task OutboxProcessor_SavesTheBatchWithoutTheShutdownToken()
    {
        using var shutdown = new CancellationTokenSource();
        var message = OutboxMessageEntity.Create("TestEvent", "{}", DateTime.UtcNow);
        var batch = CreateBatch([message]);

        batch.EventSerializer.Deserialize(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new Ok<IIntegrationEvent, TextError>(Substitute.For<IIntegrationEvent>()));
        batch.Dispatcher.DispatchAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                shutdown.Cancel();
                return Task.CompletedTask;
            });

        await batch.Processor.ProcessAsync(shutdown.Token);

        message.State.Should().Be(OutboxMessageState.Processed);
        await batch.UnitOfWork.Received(1).SaveChangesAsync(CancellationToken.None);
    }

    private static OutboxBatch CreateBatch(List<IOutboxMessage> messages)
    {
        var options = Substitute.For<IOptions<OutboxProcessorOptions>>();
        var uowFactory = Substitute.For<IUnitOfWorkFactory>();
        var uow = Substitute.For<IUnitOfWork>();
        var repository = Substitute.For<IOutboxMessageRepository>();
        var eventSerializer = Substitute.For<IEventSerializer>();
        var dispatcher = Substitute.For<IIntegrationEventDispatcher>();

        options.Value.Returns(new OutboxProcessorOptions { BatchSize = messages.Count });
        uowFactory.CreateAsync().Returns(uow);
        uow.GetRepository<IOutboxMessageRepository>().Returns(repository);
        repository.GetPendingAsync(Arg.Any<int>()).Returns(messages);

        var processor = new OutboxProcessor(
            Substitute.For<ILogger<OutboxProcessor>>(),
            options,
            uowFactory,
            eventSerializer,
            dispatcher,
            TimeProvider.System,
            Substitute.For<IOutboxMetrics>());

        return new OutboxBatch(processor, uow, eventSerializer, dispatcher);
    }

    private sealed record OutboxBatch(
        OutboxProcessor Processor,
        IUnitOfWork UnitOfWork,
        IEventSerializer EventSerializer,
        IIntegrationEventDispatcher Dispatcher);
}