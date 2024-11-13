using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Kotori.SharedCore.Tests;

public class ServicesDomainEventDispatcherTests
{
    [Fact]
    public async Task DispatchesEvents()
    {
        var services = new ServiceCollection();

        var handlers = Enumerable
            .Range(0, Random.Shared.Next(10))
            .Select(_ => new TestDomainEventHandler())
            .ToList();
        
        handlers.ForEach(handler => services.AddScoped<IDomainEventHandler<TestDomainEvent>>(_ => handler));

        services.AddScoped<IDomainEventDispatcher, ServicesDomainEventDispatcher>();
        
        var serviceProvider = services.BuildServiceProvider();
        
        var domainEventDispatcher = serviceProvider.GetRequiredService<IDomainEventDispatcher>();

        await domainEventDispatcher.DispatchAsync(new TestDomainEvent());

        handlers.All(handler => handler.Handled).Should().BeTrue();
    }
}