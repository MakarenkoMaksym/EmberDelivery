using EmberDelivery.BuildingBlocks.Application.Abstractions.Identity;
using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.BuildingBlocks.Infrastructure.Identity;
using EmberDelivery.BuildingBlocks.Infrastructure.Presentation;
using EmberDelivery.Ordering.Application.Orders.Queries.GetOrderById;

using MediatR;

namespace EmberDelivery.Ordering.Api.Endpoints.Orders;

public sealed class GetOrderByIdEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("orders/{id:guid}", async (
            Guid id,
            ICurrentUserService currentUser,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            GetOrderByIdQuery query = new(id, currentUser.GetUserId());
            Result<OrderResponse> result = await sender.Send(query, cancellationToken);

            return result.Match(Results.Ok);
        })
        .WithName("GetOrderById")
        .WithTags(EndpointTags.Orders)
        .RequireAuthorization(AuthorizationPolicies.RequireCustomer)
        .Produces<OrderResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
