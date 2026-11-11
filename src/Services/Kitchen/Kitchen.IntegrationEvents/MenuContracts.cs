namespace EmberDelivery.Kitchen.IntegrationEvents;

public static class MenuContracts
{
    public sealed record RestaurantMenuSnapshotUpdatedIntegrationEvent(
    Guid RestaurantId,
    string SnapshotPayloadJson,
    DateTimeOffset OccurredAtUtc);
}
