using FluentAssertions;
using Kotori.SharedCore.EntityFrameworkCore.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kotori.SharedCore.Tests;

public class OutboxServiceCollectionExtensionsTests
{
    private static ServiceProvider CreateServiceProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();

        services.AddOutbox(configuration);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddOutbox_BindsBackgroundOutboxProcessorOptions()
    {
        using var serviceProvider = CreateServiceProvider(new Dictionary<string, string?>
        {
            ["BackgroundOutboxProcessor:DelayMs"] = "1234",
            ["BackgroundOutboxCleaner:DelayMs"] = "4321"
        });

        serviceProvider.GetRequiredService<IOptions<BackgroundOutboxProcessorOptions>>().Value.DelayMs.Should().Be(1234);
        serviceProvider.GetRequiredService<IOptions<BackgroundOutboxCleanerOptions>>().Value.DelayMs.Should().Be(4321);
    }

    [Fact]
    public void AddOutbox_BindsOutboxProcessorRetryOptions()
    {
        using var serviceProvider = CreateServiceProvider(new Dictionary<string, string?>
        {
            ["OutboxProcessor:BatchSize"] = "50",
            ["OutboxProcessor:MaxAttempts"] = "4",
            ["OutboxProcessor:RetryBaseDelay"] = "00:00:30",
            ["OutboxProcessor:RetryMaxDelay"] = "01:00:00"
        });

        var options = serviceProvider.GetRequiredService<IOptions<OutboxProcessorOptions>>().Value;

        options.BatchSize.Should().Be(50);
        options.MaxAttempts.Should().Be(4);
        options.RetryBaseDelay.Should().Be(TimeSpan.FromSeconds(30));
        options.RetryMaxDelay.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void AddOutbox_UsesDefaultRetryOptions_WhenUnbound()
    {
        using var serviceProvider = CreateServiceProvider(new Dictionary<string, string?>
        {
            ["OutboxProcessor:BatchSize"] = "100"
        });

        var options = serviceProvider.GetRequiredService<IOptions<OutboxProcessorOptions>>().Value;

        options.BatchSize.Should().Be(100);
        options.MaxAttempts.Should().Be(10);
        options.RetryBaseDelay.Should().Be(TimeSpan.FromSeconds(5));
        options.RetryMaxDelay.Should().Be(TimeSpan.FromMinutes(10));
        serviceProvider.GetRequiredService<IOptions<BackgroundOutboxProcessorOptions>>().Value.DelayMs.Should().Be(500);
    }
}