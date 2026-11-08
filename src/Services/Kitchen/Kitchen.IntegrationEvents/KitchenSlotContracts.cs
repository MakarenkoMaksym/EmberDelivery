namespace EmberDelivery.Kitchen.IntegrationEvents;

public class KitchenSlotContracts
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
}
