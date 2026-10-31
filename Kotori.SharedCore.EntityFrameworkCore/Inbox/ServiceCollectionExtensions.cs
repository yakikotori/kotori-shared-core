using Kotori.SharedCore.Inbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kotori.SharedCore.EntityFrameworkCore.Inbox;

public static class ServiceCollectionExtensions
{
    public static void AddInbox(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IRepositoryFactory<IInboxMessageRepository>, EfRepositoryFactory<EfInboxMessageRepository>>();

        services.AddSingleton<IInbox, Inbox>();

        services.Configure<InboxCleanerOptions>(configuration.GetSection(InboxCleanerOptions.SectionName));
        services.AddSingleton<IInboxCleaner, InboxCleaner>();

        services.Configure<BackgroundInboxCleanerOptions>(configuration.GetSection(BackgroundInboxCleanerOptions.SectionName));
        services.AddHostedService<BackgroundInboxCleaner>();
    }
}
