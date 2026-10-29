using Kotori.SharedCore.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace Kotori.SharedCore.Rebus;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRebusIntegrationEventDispatcher(this IServiceCollection services)
        => services.AddSingleton<IIntegrationEventDispatcher, RebusIntegrationEventDispatcher>();
}
