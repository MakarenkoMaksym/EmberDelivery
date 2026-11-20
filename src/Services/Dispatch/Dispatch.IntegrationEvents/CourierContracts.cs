namespace EmberDelivery.Dispatch.IntegrationEvents;

public static class CourierContracts
{
    public sealed record StartCourierMatchingCommand(
        Guid OrderId,
    Guid RestaurantId,
    Guid CustomerId);

    public sealed record CourierAssignedIntegrationEvent(
        Guid OrderId,
        Guid CourierId);

    public sealed record OrderPickedUpIntegrationEvent(
        Guid OrderId,
        Guid CourierId);

    public sealed record OrderDeliveredIntegrationEvent(
        Guid OrderId,
        Guid CourierId);

    public sealed record CourierMatchingFailedIntegrationEvent(
        Guid OrderId,
        string Reason);

    // Covers both an ongoing search and a courier already on the way, so Dispatch must accept it in any state and be
    // idempotent.
    public sealed record CancelDeliveryCommand(
        Guid OrderId,
        string Reason);
}
