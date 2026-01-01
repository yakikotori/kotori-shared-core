using Kotori.SharedCore.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kotori.SharedCore.EntityFrameworkCore.Outbox;

public static class ServiceCollectionExtensions
{
    public static void AddOutbox(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IRepositoryFactory<IOutboxMessageRepository>, EfRepositoryFactory<EfOutboxMessageRepository>>();
        
        services.AddSingleton<IEventSerializer, JsonEventSerializer>();
        
        services.Configure<OutboxProcessorOptions>(configuration.GetSection(OutboxProcessorOptions.SectionName));
        services.AddSingleton<IOutboxProcessor, OutboxProcessor>();
        
        services.Configure<BackgroundOutboxCleanerOptions>(configuration.GetSection(BackgroundOutboxCleanerOptions.SectionName));
        services.AddHostedService<BackgroundOutboxProcessor>();
        
        services.Configure<OutboxCleanerOptions>(configuration.GetSection(OutboxCleanerOptions.SectionName));
        services.AddSingleton<IOutboxCleaner, OutboxCleaner>();
        
        services.Configure<BackgroundOutboxCleanerOptions>(configuration.GetSection(BackgroundOutboxCleanerOptions.SectionName));
        services.AddHostedService<BackgroundOutboxCleaner>();
    }
}