namespace EmberDelivery.Ordering.IntegrationEvents;

public class OrderSagaLifecycleEvents
{
    public sealed record OrderPaymentConfirmedIntegrationEvent(
    Guid OrderId);

    public sealed record OrderCookingStartedIntegrationEvent(
        Guid OrderId,
        int EstimatedPrepMinutes);

    public sealed record OrderDeliveryStartedIntegrationEvent(
        Guid OrderId,
        Guid CourierId);

    public sealed record OrderFulfillmentCompletedIntegrationEvent(
        Guid OrderId,
        Guid CourierId);

    public sealed record OrderFulfillmentFailedIntegrationEvent(
        Guid OrderId,
        string Reason);
}
