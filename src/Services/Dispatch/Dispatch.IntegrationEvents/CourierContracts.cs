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
}
