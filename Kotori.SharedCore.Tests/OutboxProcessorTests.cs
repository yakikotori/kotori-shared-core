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
            integrationEventDispatcher, timeProvider);

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
            integrationEventDispatcher, timeProvider);

        await outboxProcessor.ProcessAsync();

        foreach (var outboxMessage in outboxMessages)
        {
            outboxMessage.State.Should().Be(OutboxMessageState.Processed);
        }

        await integrationEventDispatcher.Received(1).DispatchAsync(mockEvent, Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OutboxProcessor_MarksAsFailed_WhenDispatcherThrowsException()
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

        integrationEventDispatcher
            .DispatchAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Dispatch failed")));

        var outboxProcessor = new OutboxProcessor(logger, options, uowFactory, eventSerializer,
            integrationEventDispatcher, timeProvider);

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
            integrationEventDispatcher, timeProvider);

        await outboxProcessor.ProcessAsync();

        successMessage.State.Should().Be(OutboxMessageState.Processed);
        failDeserializeMessage.State.Should().Be(OutboxMessageState.Failed);
        failDispatchMessage.State.Should().Be(OutboxMessageState.Failed);
    }
}