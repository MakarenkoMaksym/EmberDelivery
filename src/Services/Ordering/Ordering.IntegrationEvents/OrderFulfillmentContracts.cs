namespace EmberDelivery.Ordering.IntegrationEvents;

public sealed record OrderSubmittedIntegrationEvent(
    Guid OrderId,
    Guid CustomerId,
    Guid RestaurantId,
    decimal TotalAmount,
    string Currency);

public sealed record OrderCancelledByCustomerIntegrationEvent(
    Guid OrderId,
    string Reason);


