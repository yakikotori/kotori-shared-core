using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Kotori.SharedCore.Tests;

public class ServicesDomainEventDispatcherTests
{
    [Fact]
    public async Task DispatchesEvents()
    {
        var services = new ServiceCollection();

        var handler = new TestDomainEventHandler();
        
        services.AddScoped<IDomainEventHandler<TestDomainEvent>>(_ => handler);

        services.AddScoped<IDomainEventDispatcher, ServicesDomainEventDispatcher>();
        
        var serviceProvider = services.BuildServiceProvider();
        
        var domainEventDispatcher = serviceProvider.GetRequiredService<IDomainEventDispatcher>();

        await domainEventDispatcher.DispatchAsync(new TestDomainEvent());
        
        handler.Handled.Should().BeTrue();
    }
}