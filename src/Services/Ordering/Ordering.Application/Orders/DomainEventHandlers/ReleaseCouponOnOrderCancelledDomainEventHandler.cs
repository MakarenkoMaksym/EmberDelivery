using EmberDelivery.BuildingBlocks.Application.Messaging;
using EmberDelivery.Ordering.Domain.Aggregates.Coupons;
using EmberDelivery.Ordering.Domain.Aggregates.Orders.Events;

namespace EmberDelivery.Ordering.Application.Orders.DomainEventHandlers;

internal sealed class ReleaseCouponOnOrderCancelledDomainEventHandler(
    ICouponRepository couponRepository) : IDomainEventHandler<OrderCancelledDomainEvent>
{
    public async Task Handle(
        OrderCancelledDomainEvent notification,
        CancellationToken cancellationToken)
    {
        if (!notification.AppliedCouponId.HasValue)
        {
            return;
        }

        // Load only the single redemption record associated with the cancelled order.
        CouponRedemption? redemption = await couponRepository.GetRedemptionByOrderIdAsync(
            notification.OrderId,
            cancellationToken);

        if (redemption is null)
        {
            return;
        }

        Coupon? coupon = await couponRepository.GetByIdAsync(
            notification.AppliedCouponId.Value,
            cancellationToken);

        if (coupon is null)
        {
            return;
        }

        coupon.ReleaseRedemption(redemption);
        couponRepository.RemoveRedemption(redemption);
    }
}
