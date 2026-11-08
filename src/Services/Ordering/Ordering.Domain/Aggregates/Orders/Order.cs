using EmberDelivery.BuildingBlocks.Domain.Abstractions;
using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.Ordering.Domain.Aggregates.Coupons;
using EmberDelivery.Ordering.Domain.Aggregates.Orders.Events;
using EmberDelivery.Ordering.Domain.Pricing;
using EmberDelivery.Ordering.Domain.ValueObjects;

namespace EmberDelivery.Ordering.Domain.Aggregates.Orders;

public sealed class Order : AggregateRoot
{
    private readonly List<OrderLine> _lines = [];

    private Order()
    {
    }

    public Guid CustomerId { get; private set; }

    public Guid RestaurantId { get; private set; }

    public OrderStatus Status { get; private set; }

    public DeliveryAddress DeliveryAddress { get; private set; } = null!;

    public Money ItemsSubtotal { get; private set; }

    public Money DeliveryFee { get; private set; }

    public Money DiscountAmount { get; private set; }

    public Money TotalPrice { get; private set; }

    public Guid? AppliedCouponId { get; private set; }

    public string? AppliedCouponCode { get; private set; }

    public Guid? AssignedCourierId { get; private set; }

    public string? CancellationReason { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? EstimatedDeliveryAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public IReadOnlyCollection<OrderLine> Lines => _lines;

    public static Result<Order> Create(
        Guid customerId,
        Guid restaurantId,
        DeliveryAddress deliveryAddress,
        IReadOnlyCollection<OrderLine> lines,
        Money minimumBasketRequirement,
        DeliveryPricingPolicy deliveryPolicy,
        Coupon? coupon,
        int customerPreviousCouponUses,
        Money expectedTotalPrice,
        TimeProvider timeProvider)
    {
        if (lines.Count == 0)
        {
            return Result.Failure<Order>(OrderErrors.EmptyBasket);
        }

        DateTimeOffset nowUtc = timeProvider.GetUtcNow();
        string currency = expectedTotalPrice.Currency;

        Money itemsSubtotal = lines.Aggregate(Money.Zero(currency), (acc, line) => acc + line.LineTotal);

        if (itemsSubtotal.Amount < minimumBasketRequirement.Amount)
        {
            return Result.Failure<Order>(
                OrderErrors.BelowMinimumBasket(minimumBasketRequirement.Amount, itemsSubtotal.Amount));
        }

        var discountAmount = Money.Zero(currency);
        if (coupon is not null)
        {
            Result<Money> couponResult = coupon.EvaluateDiscount(
                restaurantId,
                itemsSubtotal,
                customerPreviousCouponUses,
                nowUtc);

            if (couponResult.IsFailure)
            {
                return Result.Failure<Order>(couponResult.Error);
            }

            discountAmount = couponResult.Value;
        }

        Money deliveryFee = deliveryPolicy.CalculateFee(itemsSubtotal);
        Money actualTotalPrice = (itemsSubtotal - discountAmount) + deliveryFee;

        if (actualTotalPrice != expectedTotalPrice)
        {
            return Result.Failure<Order>(
                OrderErrors.PriceChanged(expectedTotalPrice.Amount, actualTotalPrice.Amount));
        }

        Order order = new()
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            RestaurantId = restaurantId,
            Status = OrderStatus.Submitted,
            DeliveryAddress = deliveryAddress,
            ItemsSubtotal = itemsSubtotal,
            DeliveryFee = deliveryFee,
            DiscountAmount = discountAmount,
            TotalPrice = actualTotalPrice,
            AppliedCouponId = coupon?.Id,
            AppliedCouponCode = coupon?.Code,
            CreatedAtUtc = nowUtc
        };

        order._lines.AddRange(lines);

        order.Raise(new OrderCreatedDomainEvent(
            order.Id,
            order.CustomerId,
            order.RestaurantId,
            order.TotalPrice,
            order.AppliedCouponId));

        return Result.Success(order);
    }

    public Result MarkAsPaid()
    {
        if (Status == OrderStatus.Paid)
        {
            return Result.Success();
        }

        if (Status != OrderStatus.Submitted)
        {
            return Result.Failure(OrderErrors.InvalidStateTransition(Status, OrderStatus.Paid));
        }

        Status = OrderStatus.Paid;
        IncrementVersion();

        return Result.Success();
    }

    public Result MarkAsCooking(int estimatedPrepMinutes, TimeProvider timeProvider)
    {
        if (Status == OrderStatus.Cooking)
        {
            return Result.Success();
        }

        if (Status != OrderStatus.Paid)
        {
            return Result.Failure(OrderErrors.InvalidStateTransition(Status, OrderStatus.Cooking));
        }

        Status = OrderStatus.Cooking;
        EstimatedDeliveryAtUtc = timeProvider.GetUtcNow().AddMinutes(estimatedPrepMinutes + 15);
        IncrementVersion();

        return Result.Success();
    }

    public Result MarkAsDelivering(Guid courierId)
    {
        if (Status == OrderStatus.Delivering)
        {
            return Result.Success();
        }

        if (Status != OrderStatus.Cooking)
        {
            return Result.Failure(OrderErrors.InvalidStateTransition(Status, OrderStatus.Delivering));
        }

        Status = OrderStatus.Delivering;
        AssignedCourierId = courierId;
        IncrementVersion();

        return Result.Success();
    }

    public Result MarkAsCompleted(TimeProvider timeProvider)
    {
        if (Status == OrderStatus.Completed)
        {
            return Result.Success();
        }

        if (Status != OrderStatus.Delivering || !AssignedCourierId.HasValue)
        {
            return Result.Failure(OrderErrors.InvalidStateTransition(Status, OrderStatus.Completed));
        }

        Status = OrderStatus.Completed;
        CompletedAtUtc = timeProvider.GetUtcNow();

        Raise(new OrderCompletedDomainEvent(Id, CustomerId, AssignedCourierId.Value));

        return Result.Success();
    }

    public Result CancelByCustomer(Guid requestingCustomerId, string? reason = null)
    {
        if (CustomerId != requestingCustomerId)
        {
            return Result.Failure(OrderErrors.ForbiddenCancellation);
        }

        if (Status == OrderStatus.Cancelled)
        {
            return Result.Success();
        }

        // Customer can only cancel before the kitchen starts preparing the food.
        if (Status is not (OrderStatus.Submitted or OrderStatus.Paid))
        {
            return Result.Failure(OrderErrors.CannotCancelInProgressOrder(Status));
        }

        Status = OrderStatus.Cancelled;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? "Cancelled by customer" : reason.Trim();

        Raise(new OrderCancelledDomainEvent(Id, CustomerId, AppliedCouponId, CancellationReason, IsCustomerInitiated: true));

        return Result.Success();
    }

    public Result CancelBySystem(string reason)
    {
        if (Status == OrderStatus.Cancelled)
        {
            return Result.Success();
        }

        if (Status == OrderStatus.Completed)
        {
            return Result.Failure(OrderErrors.AlreadyTerminalState);
        }

        Status = OrderStatus.Cancelled;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? "Cancelled by system" : reason.Trim();

        Raise(new OrderCancelledDomainEvent(Id, CustomerId, AppliedCouponId, CancellationReason, IsCustomerInitiated: false));

        return Result.Success();
    }
}
