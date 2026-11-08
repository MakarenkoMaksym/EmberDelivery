using EmberDelivery.BuildingBlocks.Application.Abstractions.Identity;
using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.BuildingBlocks.Infrastructure.Presentation;
using EmberDelivery.Ordering.Application.Orders.Commands.CancelOrderByCustomer;

using MediatR;

namespace EmberDelivery.Ordering.Api.Endpoints.Orders;

public sealed class CancelOrderEndpoint : IEndpoint
{
    public sealed record CancelOrderRequest(string? Reason);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("orders/{id:guid}/cancel", async (
            Guid id,
            CancelOrderRequest request,
            ICurrentUserService currentUser,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            CancelOrderByCustomerCommand command = new(id, currentUser.GetUserId(), request.Reason);
            Result result = await sender.Send(command, cancellationToken);

            return result.Match(Results.NoContent);
        })
        .WithName("CancelOrder")
        .WithTags(EndpointTags.Orders)
        .RequireAuthorization("RequireCustomer")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
