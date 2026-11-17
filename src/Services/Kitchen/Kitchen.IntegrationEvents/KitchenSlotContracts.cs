namespace EmberDelivery.Kitchen.IntegrationEvents;

public static class KitchenSlotContracts
{
    public sealed record ConfirmKitchenCapacityCommand(
    Guid OrderId,
    Guid RestaurantId);

    public sealed record OrderAcceptedByKitchenIntegrationEvent(
        Guid OrderId,
        int EstimatedPrepMinutes);

    public sealed record KitchenRejectedOrderIntegrationEvent(
        Guid OrderId,
        string Reason);

    // Kitchen must treat it as idempotent and as a no-op for orders it never accepted: the saga re-sends it for every
    // duplicate or late acceptance that reaches an already cancelled order.
    public sealed record CancelKitchenOrderCommand(
        Guid OrderId,
        string Reason);
}
