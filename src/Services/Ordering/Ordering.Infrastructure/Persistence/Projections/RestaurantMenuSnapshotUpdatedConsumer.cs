using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using StackExchange.Redis;

using static EmberDelivery.Kitchen.IntegrationEvents.MenuContracts;

namespace EmberDelivery.Ordering.Infrastructure.Persistence.Projections;

public sealed class RestaurantMenuSnapshotUpdatedConsumer(
    OrderingDbContext dbContext,
    IConnectionMultiplexer redisMultiplexer,
    ILogger<RestaurantMenuSnapshotUpdatedConsumer> logger) : IConsumer<RestaurantMenuSnapshotUpdatedIntegrationEvent>
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    public async Task Consume(ConsumeContext<RestaurantMenuSnapshotUpdatedIntegrationEvent> context)
    {
        RestaurantMenuSnapshotUpdatedIntegrationEvent message = context.Message;

        RestaurantMenuSnapshotRecord? existing = await dbContext.RestaurantMenuSnapshots
            .FirstOrDefaultAsync(x => x.RestaurantId == message.RestaurantId, context.CancellationToken);

        if (existing is null)
        {
            existing = new RestaurantMenuSnapshotRecord
            {
                RestaurantId = message.RestaurantId,
                PayloadJson = message.SnapshotPayloadJson,
                UpdatedAtUtc = message.OccurredAtUtc
            };
            dbContext.RestaurantMenuSnapshots.Add(existing);
        }
        else if (message.OccurredAtUtc >= existing.UpdatedAtUtc)
        {
            existing.PayloadJson = message.SnapshotPayloadJson;
            existing.UpdatedAtUtc = message.OccurredAtUtc;
        }

        await dbContext.SaveChangesAsync(context.CancellationToken);

        try
        {
            IDatabase redisDb = redisMultiplexer.GetDatabase();
            string cacheKey = RestaurantMenuSnapshotReader.GetCacheKey(message.RestaurantId);
            await redisDb.StringSetAsync(cacheKey, message.SnapshotPayloadJson, CacheTtl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to update Redis cache for restaurant {RestaurantId} after PostgreSQL projection update.",
                message.RestaurantId);
        }
    }
}
