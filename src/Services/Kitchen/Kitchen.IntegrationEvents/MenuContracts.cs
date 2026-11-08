namespace EmberDelivery.Kitchen.IntegrationEvents;

public class MenuContracts
{
    public sealed record RestaurantMenuSnapshotUpdatedIntegrationEvent(
    Guid RestaurantId,
    string SnapshotPayloadJson,
    DateTimeOffset OccurredAtUtc);
}
