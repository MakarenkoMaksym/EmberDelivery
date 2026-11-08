using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.Ordering.Domain.Aggregates.Coupons;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;
using EmberDelivery.Ordering.Domain.Pricing;
using EmberDelivery.Ordering.Domain.ValueObjects;

using FluentAssertions;

using Microsoft.Extensions.Time.Testing;

namespace EmberDelivery.Ordering.UnitTests.TestUtils;

internal sealed class OrderTestBuilder
{
    public static readonly DateTimeOffset DefaultNowUtc = new(2026, 11, 1, 14, 0, 0, TimeSpan.Zero);

    // Hardcoded so tests never re-derive pricing: it matches the default lines and delivery policy below.
    public static readonly Money DefaultExpectedTotalPrice = new(450m);
    public static readonly Guid DefaultCourierId = Guid.NewGuid();
    public const int DefaultEstimatedPrepMinutes = 20;
    public const int CookingDeliveryBufferMinutes = 15;

    private readonly DeliveryAddress _deliveryAddress = CreateDefaultAddress();
    private List<OrderLine> _lines = [CreateDefaultOrderLine(new Money(200m), quantity: 2)];
    private Money _minimumBasketRequirement = new(300m);
    private DeliveryPricingPolicy _deliveryPolicy = new(
        baseFee: new Money(50m),
        surgeMultiplier: 1.0m,
        freeDeliveryThreshold: new Money(500m));
    private Coupon? _coupon;
    private int _customerPreviousCouponUses;
    private Money _expectedTotalPrice = DefaultExpectedTotalPrice;

    public FakeTimeProvider TimeProvider { get; } = new(DefaultNowUtc);

    public Guid CustomerId { get; } = Guid.NewGuid();

    public Guid RestaurantId { get; } = Guid.NewGuid();

    public static DeliveryAddress CreateDefaultAddress() =>
        DeliveryAddress.Create("Khreshchatyk", "22", 50.4501, 30.5234).Value;

    public static OrderLine CreateDefaultOrderLine(Money unitPrice, int quantity = 1) =>
        OrderLine.Create(
            Guid.NewGuid(),
            "Signature Smash Burger",
            unitPrice,
            quantity,
            []).Value;

    public OrderTestBuilder WithLines(params OrderLine[] lines)
    {
        _lines = [.. lines];
        return this;
    }

    public OrderTestBuilder WithMinimumBasketRequirement(Money minimumBasket)
    {
        _minimumBasketRequirement = minimumBasket;
        return this;
    }

    public OrderTestBuilder WithDeliveryPolicy(DeliveryPricingPolicy policy)
    {
        _deliveryPolicy = policy;
        return this;
    }

    public OrderTestBuilder WithCoupon(Coupon coupon, int previousCustomerUses = 0)
    {
        _coupon = coupon;
        _customerPreviousCouponUses = previousCustomerUses;
        return this;
    }

    public OrderTestBuilder WithExpectedTotalPrice(Money expectedTotal)
    {
        _expectedTotalPrice = expectedTotal;
        return this;
    }

    public Result<Order> CreateOrder()
    {
        return Order.Create(
            CustomerId,
            RestaurantId,
            _deliveryAddress,
            _lines,
            _minimumBasketRequirement,
            _deliveryPolicy,
            _coupon,
            _customerPreviousCouponUses,
            _expectedTotalPrice,
            TimeProvider);
    }

    public Order BuildInState(OrderStatus status)
    {
        Order order = CreateOrder().Value;

        if (status == OrderStatus.Cancelled)
        {
            order.CancelBySystem("Arranged cancellation").IsSuccess.Should().BeTrue();
            order.ClearDomainEvents();
            return order;
        }

        // Each step is driven through the real domain transition so the order is never in an unreachable state.
        if (status >= OrderStatus.Paid)
        {
            order.MarkAsPaid().IsSuccess.Should().BeTrue();
        }

        if (status >= OrderStatus.Cooking)
        {
            order.MarkAsCooking(DefaultEstimatedPrepMinutes, TimeProvider).IsSuccess.Should().BeTrue();
        }

        if (status >= OrderStatus.Delivering)
        {
            order.MarkAsDelivering(DefaultCourierId).IsSuccess.Should().BeTrue();
        }

        if (status >= OrderStatus.Completed)
        {
            order.MarkAsCompleted(TimeProvider).IsSuccess.Should().BeTrue();
        }

        // Arrange-phase events must not leak into assertions about the Act step.
        order.ClearDomainEvents();
        return order;
    }

    public Order BuildSubmitted() => BuildInState(OrderStatus.Submitted);

    public Order BuildPaid() => BuildInState(OrderStatus.Paid);

    public Order BuildCooking() => BuildInState(OrderStatus.Cooking);

    public Order BuildDelivering() => BuildInState(OrderStatus.Delivering);

    public Order BuildCompleted() => BuildInState(OrderStatus.Completed);

    public Order BuildCancelled() => BuildInState(OrderStatus.Cancelled);
}
