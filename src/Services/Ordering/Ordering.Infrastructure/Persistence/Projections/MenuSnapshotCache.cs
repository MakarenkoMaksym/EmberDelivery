namespace EmberDelivery.Ordering.Infrastructure.Persistence.Projections;

// The reader and the projection consumer must agree on key and lifetime, otherwise invalidation would miss entries.
internal static class MenuSnapshotCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromHours(6);

    public static string KeyFor(Guid restaurantId) => $"ordering:menu-snapshot:{restaurantId:N}";
}
