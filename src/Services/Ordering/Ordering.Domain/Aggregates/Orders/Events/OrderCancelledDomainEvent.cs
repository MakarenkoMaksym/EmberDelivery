using EmberDelivery.BuildingBlocks.Domain.Abstractions;

namespace EmberDelivery.Ordering.Domain.Aggregates.Orders.Events;

public sealed record OrderCancelledDomainEvent(
    Guid OrderId,
    Guid CustomerId,
    Guid? AppliedCouponId,
    string Reason,
    bool IsCustomerInitiated = false) : IDomainEvent;
