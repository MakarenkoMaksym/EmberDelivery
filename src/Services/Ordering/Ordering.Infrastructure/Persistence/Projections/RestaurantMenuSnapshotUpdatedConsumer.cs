using EmberDelivery.Ordering.Infrastructure.Messaging;

using MassTransit;

using Microsoft.EntityFrameworkCore;

using StackExchange.Redis;

using static EmberDelivery.Kitchen.IntegrationEvents.MenuContracts;

namespace EmberDelivery.Ordering.Infrastructure.Persistence.Projections;

public sealed class RestaurantMenuSnapshotUpdatedConsumer(
    OrderingDbContext dbContext,
    IConnectionMultiplexer redisMultiplexer,
    PostCommitActions postCommitActions) : IConsumer<RestaurantMenuSnapshotUpdatedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<RestaurantMenuSnapshotUpdatedIntegrationEvent> context)
    {
        RestaurantMenuSnapshotUpdatedIntegrationEvent message = context.Message;

        RestaurantMenuSnapshotRecord? existing = await dbContext.RestaurantMenuSnapshots
            .FirstOrDefaultAsync(x => x.RestaurantId == message.RestaurantId, context.CancellationToken);

        if (existing is null)
        {
            dbContext.RestaurantMenuSnapshots.Add(new RestaurantMenuSnapshotRecord
            {
                RestaurantId = message.RestaurantId,
                PayloadJson = message.SnapshotPayloadJson,
                UpdatedAtUtc = message.OccurredAtUtc
            });
        }
        else if (message.OccurredAtUtc >= existing.UpdatedAtUtc)
        {
            existing.PayloadJson = message.SnapshotPayloadJson;
            existing.UpdatedAtUtc = message.OccurredAtUtc;
        }
        else
        {
            // An out-of-order snapshot changes nothing, so the cache still mirrors the stored projection.
            return;
        }

        await dbContext.SaveChangesAsync(context.CancellationToken);

        // Invalidating instead of writing lets the reader warm the cache from committed data only; writing the payload
        // here would race with the outbox commit and could publish a value the database later rejects.
        string cacheKey = MenuSnapshotCache.KeyFor(message.RestaurantId);
        postCommitActions.Enqueue(_ => redisMultiplexer.GetDatabase().KeyDeleteAsync(cacheKey));
    }
}
