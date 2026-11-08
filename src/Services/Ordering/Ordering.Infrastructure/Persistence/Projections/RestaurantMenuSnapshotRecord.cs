namespace EmberDelivery.Ordering.Infrastructure.Persistence.Projections;

public sealed class RestaurantMenuSnapshotRecord
{
    public Guid RestaurantId { get; set; }

    public string PayloadJson { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
