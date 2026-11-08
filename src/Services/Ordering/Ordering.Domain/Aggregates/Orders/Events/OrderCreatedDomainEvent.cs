using EmberDelivery.BuildingBlocks.Domain.Abstractions;
using EmberDelivery.Ordering.Domain.ValueObjects;

namespace EmberDelivery.Ordering.Domain.Aggregates.Orders.Events;

public sealed record OrderCreatedDomainEvent(
    Guid OrderId,
    Guid CustomerId,
    Guid RestaurantId,
    Money TotalPrice,
    Guid? AppliedCouponId) : IDomainEvent;
