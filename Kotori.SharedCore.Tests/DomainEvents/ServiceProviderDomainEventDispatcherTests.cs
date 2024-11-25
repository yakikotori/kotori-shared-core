using FluentAssertions;
using Kotori.SharedCore.DomainEvents;
using Microsoft.Extensions.DependencyInjection;

namespace Kotori.SharedCore.Tests.DomainEvents;

public class ServiceProviderDomainEventDispatcherTests
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

        services.AddScoped<IDomainEventDispatcher, ServiceProviderDomainEventDispatcher>();
        
        var serviceProvider = services.BuildServiceProvider();
        
        var domainEventDispatcher = serviceProvider.GetRequiredService<IDomainEventDispatcher>();

        await domainEventDispatcher.DispatchAsync(new TestDomainEvent());

        handlers.All(handler => handler.Handled).Should().BeTrue();
    }
}