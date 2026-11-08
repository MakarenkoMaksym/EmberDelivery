using EmberDelivery.Ordering.Domain.Pricing;

namespace EmberDelivery.Ordering.Application.Abstractions;

public interface IRestaurantMenuSnapshotReader
{
    Task<RestaurantMenuSnapshot?> GetByRestaurantIdAsync(
        Guid restaurantId,
        CancellationToken cancellationToken = default);
}
