using EmberDelivery.BuildingBlocks.Application.Messaging;
using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.Ordering.Application.Abstractions;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;

namespace EmberDelivery.Ordering.Application.Orders.Queries.GetOrderById;

public sealed record OrderModifierResponse(
    Guid ModifierId,
    string Name,
    decimal Price);

public sealed record OrderLineResponse(
    Guid Id,
    Guid MenuItemId,
    string Name,
    decimal BaseUnitPrice,
    int Quantity,
    decimal LineTotal,
    IReadOnlyList<OrderModifierResponse> Modifiers);

public sealed record OrderResponse(
    Guid Id,
    Guid CustomerId,
    Guid RestaurantId,
    string Status,
    string Street,
    string Building,
    string? Apartment,
    double Latitude,
    double Longitude,
    decimal ItemsSubtotal,
    decimal DeliveryFee,
    decimal DiscountAmount,
    decimal TotalPrice,
    string Currency,
    string? AppliedCouponCode,
    Guid? AssignedCourierId,
    string? CancellationReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? EstimatedDeliveryAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<OrderLineResponse> Lines);

public sealed record GetOrderByIdQuery(
    Guid OrderId,
    Guid RequestingCustomerId) : IQuery<OrderResponse>;

internal sealed class GetOrderByIdQueryHandler(
    IOrderReadService orderReadService) : IQueryHandler<GetOrderByIdQuery, OrderResponse>
{
    public async Task<Result<OrderResponse>> Handle(
        GetOrderByIdQuery request,
        CancellationToken cancellationToken)
    {
        OrderResponse? order = await orderReadService.GetByIdAsync(request.OrderId, cancellationToken);

        if (order is null)
        {
            return Result.Failure<OrderResponse>(OrderErrors.NotFound(request.OrderId));
        }

        if (order.CustomerId != request.RequestingCustomerId)
        {
            return Result.Failure<OrderResponse>(
                Error.Forbidden("Order.ForbiddenAccess", "You do not have permission to view this order."));
        }

        return Result.Success(order);
    }
}
