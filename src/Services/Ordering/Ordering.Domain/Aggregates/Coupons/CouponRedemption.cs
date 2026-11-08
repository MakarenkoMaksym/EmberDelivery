using EmberDelivery.BuildingBlocks.Domain.Abstractions;
using EmberDelivery.Ordering.Domain.ValueObjects;

namespace EmberDelivery.Ordering.Domain.Aggregates.Coupons;

public sealed class CouponRedemption : Entity
{
    private CouponRedemption()
    {
    }

    internal CouponRedemption(
        Guid id,
        Guid couponId,
        Guid customerId,
        Guid orderId,
        Money discountApplied,
        DateTimeOffset redeemedAtUtc)
    {
        Id = id;
        CouponId = couponId;
        CustomerId = customerId;
        OrderId = orderId;
        DiscountApplied = discountApplied;
        RedeemedAtUtc = redeemedAtUtc;
    }

    public Guid CouponId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid OrderId { get; private set; }

    public Money DiscountApplied { get; private set; }

    public DateTimeOffset RedeemedAtUtc { get; private set; }
}
