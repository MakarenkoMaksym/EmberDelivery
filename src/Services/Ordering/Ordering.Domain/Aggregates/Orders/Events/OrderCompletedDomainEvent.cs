using EmberDelivery.BuildingBlocks.Domain.Abstractions;

namespace EmberDelivery.Ordering.Domain.Aggregates.Orders.Events;

public sealed record OrderCompletedDomainEvent(
    Guid OrderId,
    Guid CustomerId,
    Guid CourierId) : IDomainEvent;
