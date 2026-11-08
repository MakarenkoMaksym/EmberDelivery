using System.Text.Json;

using EmberDelivery.Ordering.Application.Abstractions;
using EmberDelivery.Ordering.Domain.Pricing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using StackExchange.Redis;

namespace EmberDelivery.Ordering.Infrastructure.Persistence.Projections;

internal sealed class RestaurantMenuSnapshotReader(
    IConnectionMultiplexer redisMultiplexer,
    OrderingDbContext dbContext,
    ILogger<RestaurantMenuSnapshotReader> logger) : IRestaurantMenuSnapshotReader
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    public async Task<RestaurantMenuSnapshot?> GetByRestaurantIdAsync(
        Guid restaurantId,
        CancellationToken cancellationToken = default)
    {
        string cacheKey = GetCacheKey(restaurantId);

        // Try fast path from Redis cache. If Redis is unreachable or slow, degrade gracefully to PostgreSQL.
        string? cachedJson = await TryReadFromRedisAsync(cacheKey, restaurantId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(cachedJson))
        {
            return JsonSerializer.Deserialize<RestaurantMenuSnapshot>(cachedJson);
        }

        // Fallback to local PostgreSQL projection table (ECST).
        RestaurantMenuSnapshotRecord? record = await dbContext.RestaurantMenuSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RestaurantId == restaurantId, cancellationToken);

        if (record is null)
        {
            return null;
        }

        // Best-effort cache repopulation; failures here must never abort order creation.
        await TryWriteToRedisAsync(cacheKey, record.PayloadJson, restaurantId);

        return JsonSerializer.Deserialize<RestaurantMenuSnapshot>(record.PayloadJson);
    }

    internal static string GetCacheKey(Guid restaurantId) => $"ordering:menu-snapshot:{restaurantId:N}";

    private async Task<string?> TryReadFromRedisAsync(
        string cacheKey,
        Guid restaurantId,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            IDatabase db = redisMultiplexer.GetDatabase();
            RedisValue value = await db.StringGetAsync(cacheKey);

            return value.HasValue ? value.ToString() : null;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                ex,
                "Redis read failed for restaurant {RestaurantId}. Falling back to local PostgreSQL snapshot.",
                restaurantId);

            return null;
        }
    }

    private async Task TryWriteToRedisAsync(string cacheKey, string payloadJson, Guid restaurantId)
    {
        try
        {
            IDatabase db = redisMultiplexer.GetDatabase();
            await db.StringSetAsync(cacheKey, payloadJson, CacheTtl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to repopulate Redis cache for restaurant {RestaurantId}.",
                restaurantId);
        }
    }
}
