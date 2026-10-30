using System.Collections.Concurrent;
using Kotori.SharedCore.IntegrationEvents;
using Rebus.Messages;
using Rebus.Pipeline;

namespace Kotori.SharedCore.Tests;

public record CrateShipped(Guid CrateId) : IntegrationEvent;

public record CrateReturned(Guid CrateId) : IntegrationEvent;

public record CrateLost(Guid CrateId, Guid CorrelationId) : IntegrationEvent, ICorrelatedEvent;

public record LaneCall(string Handler, IIntegrationEvent Event, string MessageId, string DeferCount, bool CanBeCancelled);

public class LaneCallLog
{
    private readonly ConcurrentQueue<LaneCall> _calls = new();

    public IReadOnlyList<LaneCall> Calls => _calls.ToArray();

    public void Add(string handler, IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var headers = MessageContext.Current.Headers;

        _calls.Enqueue(new LaneCall(
            handler,
            integrationEvent,
            headers[Headers.MessageId],
            headers.GetValueOrDefault(Headers.DeferCount, "0"),
            cancellationToken.CanBeCanceled));
    }

    public async Task WaitForAsync(int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        while (_calls.Count < count)
        {
            await Task.Delay(50, timeout.Token);
        }

        await Task.Delay(500);
    }
}

public class LaneAShippedHandler : IIntegrationEventHandler<CrateShipped>
{
    private readonly LaneCallLog _log;

    public LaneAShippedHandler(LaneCallLog log)
    {
        _log = log;
    }

    public Task HandleAsync(CrateShipped integrationEvent, CancellationToken cancellationToken)
    {
        _log.Add(nameof(LaneAShippedHandler), integrationEvent, cancellationToken);
        return Task.CompletedTask;
    }
}

public class LaneAShippedAuditHandler : IIntegrationEventHandler<CrateShipped>
{
    private readonly LaneCallLog _log;

    public LaneAShippedAuditHandler(LaneCallLog log)
    {
        _log = log;
    }

    public Task HandleAsync(CrateShipped integrationEvent, CancellationToken cancellationToken)
    {
        _log.Add(nameof(LaneAShippedAuditHandler), integrationEvent, cancellationToken);
        return Task.CompletedTask;
    }
}

public class LaneAReturnedHandler : IIntegrationEventHandler<CrateReturned>
{
    private readonly LaneCallLog _log;

    public LaneAReturnedHandler(LaneCallLog log)
    {
        _log = log;
    }

    public Task HandleAsync(CrateReturned integrationEvent, CancellationToken cancellationToken)
    {
        _log.Add(nameof(LaneAReturnedHandler), integrationEvent, cancellationToken);
        return Task.CompletedTask;
    }
}

public class LaneBShippedHandler : IIntegrationEventHandler<CrateShipped>
{
    private readonly LaneCallLog _log;

    public LaneBShippedHandler(LaneCallLog log)
    {
        _log = log;
    }

    public Task HandleAsync(CrateShipped integrationEvent, CancellationToken cancellationToken)
    {
        _log.Add(nameof(LaneBShippedHandler), integrationEvent, cancellationToken);
        return Task.CompletedTask;
    }
}

public class FailingShippedHandler : IIntegrationEventHandler<CrateShipped>
{
    private readonly LaneCallLog _log;

    public FailingShippedHandler(LaneCallLog log)
    {
        _log = log;
    }

    public Task HandleAsync(CrateShipped integrationEvent, CancellationToken cancellationToken)
    {
        _log.Add(nameof(FailingShippedHandler), integrationEvent, cancellationToken);
        throw new InvalidOperationException("Courier API unavailable");
    }
}

public class LabelPrinter
{
    public string Print(Guid crateId) => $"RETURN-{crateId}";
}

public class LabelPrintingReturnedHandler : IIntegrationEventHandler<CrateReturned>
{
    private readonly LabelPrinter _printer;
    private readonly LaneCallLog _log;

    public LabelPrintingReturnedHandler(LabelPrinter printer, LaneCallLog log)
    {
        _printer = printer;
        _log = log;
    }

    public Task HandleAsync(CrateReturned integrationEvent, CancellationToken cancellationToken)
    {
        _log.Add(_printer.Print(integrationEvent.CrateId), integrationEvent, cancellationToken);
        return Task.CompletedTask;
    }
}
