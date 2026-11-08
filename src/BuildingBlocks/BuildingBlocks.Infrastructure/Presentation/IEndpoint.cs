using Microsoft.AspNetCore.Routing;

namespace EmberDelivery.BuildingBlocks.Infrastructure.Presentation;

public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
