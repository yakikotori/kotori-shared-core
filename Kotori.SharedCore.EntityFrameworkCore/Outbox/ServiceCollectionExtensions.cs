using Kotori.SharedCore.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kotori.SharedCore.EntityFrameworkCore.Outbox;

public static class ServiceCollectionExtensions
{
    public static void AddEfDomainOutbox(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OutboxProcessorOptions>(configuration.GetSection("OutboxProcessor"));
        services.Configure<OutboxCleanerOptions>(configuration.GetSection("OutboxCleaner"));

        services.AddSingleton<IRepositoryFactory<IOutboxMessageRepository>, EfRepositoryFactory<EfOutboxMessageRepository>>();
        
        services.AddSingleton<IOutboxProcessor, OutboxProcessor>();
        services.AddSingleton<IOutboxCleaner, OutboxCleaner>();
        
        services.AddSingleton<IEventSerializer, JsonEventSerializer>();
    }
}