using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Rebus.Config;

namespace Kotori.SharedCore.Rebus;

public static class HostBuilderExtensions
{
    public static IHostBuilder AddIntegrationEventLane(
        this IHostBuilder hostBuilder,
        Func<RebusConfigurer, RebusConfigurer> configure,
        string errorQueueName,
        IntegrationEventRetryPolicy retryPolicy,
        Action<IntegrationEventLane> configureLane)
    {
        var lane = new IntegrationEventLane();

        configureLane(lane);

        if (lane.EventTypes.Count == 0)
        {
            throw new InvalidOperationException("An integration event lane needs at least one handler");
        }

        return hostBuilder
            .ConfigureServices((_, services) => services.TryAddSingleton<IIntegrationEventLaneMetrics, IntegrationEventLaneMetrics>())
            .AddRebusService(
                services => lane.Register(services, configure, errorQueueName, retryPolicy),
                lane.ForwardedSingletonTypes);
    }
}
