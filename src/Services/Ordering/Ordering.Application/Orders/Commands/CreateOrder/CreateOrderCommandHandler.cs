using EmberDelivery.BuildingBlocks.Application.Abstractions;
using EmberDelivery.BuildingBlocks.Application.Messaging;
using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.Ordering.Application.Abstractions;
using EmberDelivery.Ordering.Domain.Aggregates.Coupons;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;
using EmberDelivery.Ordering.Domain.Pricing;
using EmberDelivery.Ordering.Domain.ValueObjects;

namespace EmberDelivery.Ordering.Application.Orders.Commands.CreateOrder;

public sealed record OrderItemRequest(
    Guid MenuItemId,
    int Quantity,
    IReadOnlyList<Guid> SelectedModifierIds);

public sealed record CreateOrderCommand(
    Guid CustomerId,
    Guid RestaurantId,
    string Street,
    string Building,
    double Latitude,
    double Longitude,
    IReadOnlyList<OrderItemRequest> Items,
    string? CouponCode,
    decimal ExpectedTotalAmount,
    string Currency = "UAH") : ICommand<Guid>;

internal sealed class CreateOrderCommandHandler(
    IRestaurantMenuSnapshotReader menuSnapshotReader,
    ICouponRepository couponRepository,
    IOrderRepository orderRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<CreateOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(
        CreateOrderCommand request,
        CancellationToken cancellationToken)
    {
        RestaurantMenuSnapshot? menuSnapshot = await menuSnapshotReader.GetByRestaurantIdAsync(
            request.RestaurantId,
            cancellationToken);

        if (menuSnapshot is null || !menuSnapshot.IsAcceptingOrders)
        {
            return Result.Failure<Guid>(
                Error.Conflict("Order.RestaurantUnavailable", "The restaurant is currently closed or unavailable."));
        }

        Result<DeliveryAddress> addressResult = DeliveryAddress.Create(
            request.Street,
            request.Building,
            request.Latitude,
            request.Longitude);

        if (addressResult.IsFailure)
        {
            return Result.Failure<Guid>(addressResult.Error);
        }

        var lineSpecs = request.Items
            .Select(item => new OrderLineSpec(item.MenuItemId, item.Quantity, item.SelectedModifierIds))
            .ToList();

        Result<IReadOnlyList<OrderLine>> linesResult = menuSnapshot.CreateOrderLines(lineSpecs);
        if (linesResult.IsFailure)
        {
            return Result.Failure<Guid>(linesResult.Error);
        }

        Coupon? coupon = null;
        int customerCouponUses = 0;

        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            string normalizedCode = request.CouponCode.Trim().ToUpperInvariant();
            coupon = await couponRepository.GetActiveByCodeAsync(normalizedCode, cancellationToken);

            if (coupon is null)
            {
                return Result.Failure<Guid>(
                    Error.NotFound("Order.CouponNotFound", $"Coupon '{normalizedCode}' was not found."));
            }

            customerCouponUses = await couponRepository.CountCustomerRedemptionsAsync(
                coupon.Id,
                request.CustomerId,
                cancellationToken);
        }

        DeliveryPricingPolicy deliveryPolicy = new(
            menuSnapshot.BaseDeliveryFee,
            surgeMultiplier: 1.0m,
            menuSnapshot.FreeDeliveryThreshold);

        Money expectedTotal = new(request.ExpectedTotalAmount, request.Currency);

        Result<Order> orderResult = Order.Create(
            request.CustomerId,
            request.RestaurantId,
            addressResult.Value,
            linesResult.Value,
            menuSnapshot.MinimumBasketRequirement,
            deliveryPolicy,
            coupon,
            customerCouponUses,
            expectedTotal,
            timeProvider);

        if (orderResult.IsFailure)
        {
            return Result.Failure<Guid>(orderResult.Error);
        }

        Order order = orderResult.Value;

        if (coupon is not null)
        {
            Result<CouponRedemption> redemptionResult = coupon.Redeem(
                request.CustomerId,
                order.Id,
                request.RestaurantId,
                order.ItemsSubtotal,
                customerCouponUses,
                timeProvider);

            if (redemptionResult.IsFailure)
            {
                return Result.Failure<Guid>(redemptionResult.Error);
            }

            couponRepository.AddRedemption(redemptionResult.Value);
        }

        orderRepository.Add(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(order.Id);
    }
}
