using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kotori.SharedCore.Endpoints;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEndpoints(this IServiceCollection services, Assembly assembly)
    {
        var endpoints = assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false } &&
                           type.GetInterface(nameof(IEndpoint)) is not null)
            .Select(type => ServiceDescriptor.Transient(typeof(IEndpoint), type))
            .ToList();
        
        services.TryAddEnumerable(endpoints);

        return services;
    }
}