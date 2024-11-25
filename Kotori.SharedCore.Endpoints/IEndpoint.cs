using Microsoft.AspNetCore.Routing;

namespace Kotori.SharedCore.Endpoints;

public interface IEndpoint
{
    void Map(IEndpointRouteBuilder routes);
}