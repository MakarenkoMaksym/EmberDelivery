using EmberDelivery.BuildingBlocks.Application.Abstractions.Identity;
using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.BuildingBlocks.Infrastructure.Identity;
using EmberDelivery.BuildingBlocks.Infrastructure.Presentation;
using EmberDelivery.Ordering.Application.Orders.Commands.CreateOrder;

using MediatR;

namespace EmberDelivery.Ordering.Api.Endpoints.Orders;

public sealed class CreateOrderEndpoint : IEndpoint
{
    public sealed record CreateOrderRequest(
        Guid RestaurantId,
        string Street,
        string Building,
        double Latitude,
        double Longitude,
        IReadOnlyList<OrderItemRequest> Items,
        string? CouponCode,
        decimal ExpectedTotalAmount,
        string Currency = "UAH");

    public sealed record CreateOrderResponse(Guid Id);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("orders", async (
            CreateOrderRequest request,
            ICurrentUserService currentUser,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            CreateOrderCommand command = new(
                currentUser.GetUserId(),
                request.RestaurantId,
                request.Street,
                request.Building,
                request.Latitude,
                request.Longitude,
                request.Items,
                request.CouponCode,
                request.ExpectedTotalAmount,
                request.Currency);

            Result<Guid> result = await sender.Send(command, cancellationToken);

            return result.Match(orderId =>
                Results.Created($"/api/v1/orders/{orderId}", new CreateOrderResponse(orderId)));
        })
        .WithName("CreateOrder")
        .WithTags(EndpointTags.Orders)
        .RequireAuthorization(AuthorizationPolicies.RequireCustomer)
        .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
