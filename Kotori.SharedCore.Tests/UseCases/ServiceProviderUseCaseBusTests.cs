using FluentAssertions;
using Kotori.SharedCore.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace Kotori.SharedCore.Tests.UseCases;

public class ServiceProviderUseCaseBusTests
{
    [Fact]
    public async Task LocatesQueries()
    {
        var services = new ServiceCollection();

        var data = new Dictionary<string, string>
        {
            {"test", "meow"}
        };
        
        services.AddScoped<IQueryHandler<TestQuery, string>>(_ => new TestQueryHandler(data));

        services.AddScoped<IUseCaseBus, ServiceProviderUseCaseBus>();

        var serviceProvider = services.BuildServiceProvider();

        var bus = serviceProvider.GetRequiredService<IUseCaseBus>();

        var response = await bus.ExecuteQueryAsync<TestQuery, string>(new TestQuery("test"));

        response.Should().Be("meow");
    }
    
    [Fact]
    public async Task LocatesCommands()
    {
        var services = new ServiceCollection();
        
        services.AddScoped<ICommandHandler<TestCommand, string>>(_ => new TestCommandHandler(input => input.Trim()));

        services.AddScoped<IUseCaseBus, ServiceProviderUseCaseBus>();

        var serviceProvider = services.BuildServiceProvider();

        var bus = serviceProvider.GetRequiredService<IUseCaseBus>();

        var response = await bus.ExecuteCommandAsync<TestCommand, string>(new TestCommand(" meow "));

        response.Should().Be("meow");
    }
}