using Kotori.SharedCore.IntegrationEvents;
using Kotori.SharedCore.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kotori.SharedCore.EntityFrameworkCore.Outbox;

public static class ServiceCollectionExtensions
{
    public static void AddOutbox(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IRepositoryFactory<IOutboxMessageRepository>, EfRepositoryFactory<EfOutboxMessageRepository>>();
        
        services.AddSingleton<IEventSerializer, JsonEventSerializer>();
        
        services.AddMetrics();
        services.TryAddSingleton<IOutboxMetrics, OutboxMetrics>();

        services.Configure<OutboxProcessorOptions>(configuration.GetSection(OutboxProcessorOptions.SectionName));
        services.AddSingleton<IOutboxProcessor, OutboxProcessor>();
        
        services.Configure<BackgroundOutboxProcessorOptions>(configuration.GetSection(BackgroundOutboxProcessorOptions.SectionName));
        services.AddHostedService<BackgroundOutboxProcessor>();
        
        services.Configure<OutboxCleanerOptions>(configuration.GetSection(OutboxCleanerOptions.SectionName));
        services.AddSingleton<IOutboxCleaner, OutboxCleaner>();
        
        services.Configure<BackgroundOutboxCleanerOptions>(configuration.GetSection(BackgroundOutboxCleanerOptions.SectionName));
        services.AddHostedService<BackgroundOutboxCleaner>();
    }

    public static IServiceCollection AddIntegrationEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIntegrationEventHandler<TEvent>, THandler>());

        return services;
    }
}