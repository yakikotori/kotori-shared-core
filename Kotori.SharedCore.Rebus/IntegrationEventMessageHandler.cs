using Kotori.SharedCore.Inbox;
using Kotori.SharedCore.IntegrationEvents;
using Microsoft.Extensions.Logging;
using Rebus.Bus;
using Rebus.Exceptions;
using Rebus.Extensions;
using Rebus.Handlers;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Retry.Simple;

namespace Kotori.SharedCore.Rebus;

public class IntegrationEventMessageHandler<TEvent> : IHandleMessages<TEvent>, IHandleMessages<IFailed<TEvent>>
    where TEvent : IIntegrationEvent
{
    private readonly IEnumerable<IIntegrationEventHandler<TEvent>> _handlers;
    private readonly IntegrationEventRetryPolicy _retryPolicy;
    private readonly IInbox _inbox;
    private readonly IntegrationEventLaneConsumer _consumer;
    private readonly IIntegrationEventLaneMetrics _metrics;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IntegrationEventMessageHandler<TEvent>> _logger;
    private readonly IMessageContext _messageContext;
    private readonly IBus _bus;

    public IntegrationEventMessageHandler(
        IEnumerable<IIntegrationEventHandler<TEvent>> handlers,
        IntegrationEventRetryPolicy retryPolicy,
        IInbox inbox,
        IntegrationEventLaneConsumer consumer,
        IIntegrationEventLaneMetrics metrics,
        TimeProvider timeProvider,
        ILogger<IntegrationEventMessageHandler<TEvent>> logger,
        IMessageContext messageContext,
        IBus bus)
    {
        _handlers = handlers;
        _retryPolicy = retryPolicy;
        _inbox = inbox;
        _consumer = consumer;
        _metrics = metrics;
        _timeProvider = timeProvider;
        _logger = logger;
        _messageContext = messageContext;
        _bus = bus;
    }

    public async Task Handle(TEvent message)
    {
        var cancellationToken = _messageContext.GetCancellationToken();

        if (await _inbox.HasProcessedAsync(message.Id, _consumer.Name, cancellationToken))
        {
            _logger.LogDebug(
                "Skipped integration event {MessageId} already processed by {Consumer}",
                message.Id,
                _consumer.Name);
            _metrics.RecordDuplicateSkipped(_consumer, typeof(TEvent));
            return;
        }

        var startedAt = _timeProvider.GetTimestamp();

        foreach (var handler in _handlers)
        {
            await handler.HandleAsync(message, cancellationToken);
        }

        var duration = _timeProvider.GetElapsedTime(startedAt);

        await _inbox.MarkProcessedAsync(message.Id, _consumer.Name, CancellationToken.None);

        _metrics.RecordHandled(_consumer, typeof(TEvent), duration);
    }

    public Task Handle(IFailed<TEvent> message)
    {
        var deferrals = DeferralsOf(message);

        return _retryPolicy.Decide(deferrals) switch
        {
            DeferIntegrationEvent defer => DeferAsync(defer.Delay),
            DeadLetterIntegrationEvent => DeadLetterAsync(
                $"Gave up after {deferrals} deferrals{Environment.NewLine}{Environment.NewLine}{message.ErrorDescription}"),
            var decision => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unknown retry decision")
        };
    }

    private async Task DeferAsync(TimeSpan delay)
    {
        await _bus.Advanced.TransportMessage.Defer(delay);

        _metrics.RecordDeferred(_consumer, typeof(TEvent), delay);
    }

    private async Task DeadLetterAsync(string errorDetails)
    {
        await _bus.Advanced.TransportMessage.Deadletter(errorDetails);

        _metrics.RecordDeadLettered(_consumer, typeof(TEvent));
    }

    private static int DeferralsOf(IFailed<TEvent> message)
        => message.Headers.TryGetValue(Headers.DeferCount, out var value) && int.TryParse(value, out var deferrals)
            ? Math.Max(deferrals, 0)
            : 0;
}
