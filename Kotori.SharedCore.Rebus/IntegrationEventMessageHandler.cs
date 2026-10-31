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
    private readonly ILogger<IntegrationEventMessageHandler<TEvent>> _logger;
    private readonly IMessageContext _messageContext;
    private readonly IBus _bus;

    public IntegrationEventMessageHandler(
        IEnumerable<IIntegrationEventHandler<TEvent>> handlers,
        IntegrationEventRetryPolicy retryPolicy,
        IInbox inbox,
        IntegrationEventLaneConsumer consumer,
        ILogger<IntegrationEventMessageHandler<TEvent>> logger,
        IMessageContext messageContext,
        IBus bus)
    {
        _handlers = handlers;
        _retryPolicy = retryPolicy;
        _inbox = inbox;
        _consumer = consumer;
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
            return;
        }

        foreach (var handler in _handlers)
        {
            await handler.HandleAsync(message, cancellationToken);
        }

        await _inbox.MarkProcessedAsync(message.Id, _consumer.Name, CancellationToken.None);
    }

    public Task Handle(IFailed<TEvent> message)
    {
        var deferrals = DeferralsOf(message);

        return _retryPolicy.Decide(deferrals) switch
        {
            DeferIntegrationEvent defer => _bus.Advanced.TransportMessage.Defer(defer.Delay),
            DeadLetterIntegrationEvent => _bus.Advanced.TransportMessage.Deadletter(
                $"Gave up after {deferrals} deferrals{Environment.NewLine}{Environment.NewLine}{message.ErrorDescription}"),
            var decision => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unknown retry decision")
        };
    }

    private static int DeferralsOf(IFailed<TEvent> message)
        => message.Headers.TryGetValue(Headers.DeferCount, out var value) && int.TryParse(value, out var deferrals)
            ? Math.Max(deferrals, 0)
            : 0;
}
