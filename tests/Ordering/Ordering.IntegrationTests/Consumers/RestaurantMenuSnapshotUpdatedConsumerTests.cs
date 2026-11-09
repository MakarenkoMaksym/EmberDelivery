using System.Text.Json.Nodes;

using EmberDelivery.Ordering.Infrastructure.Persistence.Projections;
using EmberDelivery.Ordering.IntegrationTests.Infrastructure;
using EmberDelivery.Ordering.IntegrationTests.TestData;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using StackExchange.Redis;

using Xunit;

using static EmberDelivery.Kitchen.IntegrationEvents.MenuContracts;

namespace EmberDelivery.Ordering.IntegrationTests.Consumers;

public sealed class RestaurantMenuSnapshotUpdatedConsumerTests(OrderingAppFixture fixture) : BaseIntegrationTest(fixture)
{
    private static readonly DateTimeOffset EarlierUtc = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LaterUtc = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Consume_FirstSnapshotForRestaurant_ShouldStoreProjectionAndWarmRedisCache()
    {
        // Arrange
        var restaurantId = Guid.NewGuid();
        string payloadJson = TestMenu.CreatePayloadJson(restaurantId);

        // Act
        await PublishAndWaitForConsumptionAsync(
            new RestaurantMenuSnapshotUpdatedIntegrationEvent(restaurantId, payloadJson, LaterUtc));

        // Assert
        RestaurantMenuSnapshotRecord record = await GetProjectionAsync(restaurantId);
        ShouldBeSameJson(record.PayloadJson, payloadJson);
        record.UpdatedAtUtc.Should().Be(LaterUtc);

        RedisValue cached = await GetRedisDatabase().StringGetAsync(CacheKey(restaurantId));
        cached.ToString().Should().Be(payloadJson);
    }

    [Fact]
    public async Task Consume_SnapshotOlderThanStoredOne_ShouldKeepNewerProjection()
    {
        // Arrange
        // Out-of-order delivery must never roll the menu back to stale prices.
        var restaurantId = Guid.NewGuid();
        string newerPayload = TestMenu.CreatePayloadJson(restaurantId, isAcceptingOrders: true);
        string olderPayload = TestMenu.CreatePayloadJson(restaurantId, isAcceptingOrders: false);
        await PublishAndWaitForConsumptionAsync(
            new RestaurantMenuSnapshotUpdatedIntegrationEvent(restaurantId, newerPayload, LaterUtc));

        // Act
        await PublishAndWaitForConsumptionAsync(
            new RestaurantMenuSnapshotUpdatedIntegrationEvent(restaurantId, olderPayload, EarlierUtc));

        // Assert
        RestaurantMenuSnapshotRecord record = await GetProjectionAsync(restaurantId);
        ShouldBeSameJson(record.PayloadJson, newerPayload);
        record.UpdatedAtUtc.Should().Be(LaterUtc);
    }

    [Fact]
    public async Task Consume_SnapshotNewerThanStoredOne_ShouldReplaceProjection()
    {
        // Arrange
        var restaurantId = Guid.NewGuid();
        string olderPayload = TestMenu.CreatePayloadJson(restaurantId, isAcceptingOrders: true);
        string newerPayload = TestMenu.CreatePayloadJson(restaurantId, isAcceptingOrders: false);
        await PublishAndWaitForConsumptionAsync(
            new RestaurantMenuSnapshotUpdatedIntegrationEvent(restaurantId, olderPayload, EarlierUtc));

        // Act
        await PublishAndWaitForConsumptionAsync(
            new RestaurantMenuSnapshotUpdatedIntegrationEvent(restaurantId, newerPayload, LaterUtc));

        // Assert
        RestaurantMenuSnapshotRecord record = await GetProjectionAsync(restaurantId);
        ShouldBeSameJson(record.PayloadJson, newerPayload);
        record.UpdatedAtUtc.Should().Be(LaterUtc);
    }

    [Fact]
    public async Task Consume_Snapshot_ShouldMakeRestaurantOrderableWithPricesFromPayload()
    {
        // Arrange
        // Proves the ECST payload survives the JSON round trip with Money values intact.
        var restaurantId = Guid.NewGuid();

        // Act
        await PublishAndWaitForConsumptionAsync(new RestaurantMenuSnapshotUpdatedIntegrationEvent(
            restaurantId,
            TestMenu.CreatePayloadJson(restaurantId),
            LaterUtc));

        // Assert
        Guid orderId = await CreateOrderAsync(restaurantId, expectedTotalAmount: TestMenu.StandardBasketTotal);
        (await GetOrderFromDbAsync(orderId)).TotalPrice.Amount.Should().Be(400m);
    }

    // The projection column is jsonb, which normalizes key order and whitespace, so only semantic equality is meaningful.
    private static void ShouldBeSameJson(string actualJson, string expectedJson) =>
        JsonNode.DeepEquals(JsonNode.Parse(actualJson), JsonNode.Parse(expectedJson))
            .Should().BeTrue($"stored payload {actualJson} should match {expectedJson}");

    // Duplicated on purpose: the cache key is a contract shared with the reader, so a silent rename must fail here.
    private static string CacheKey(Guid restaurantId) => $"ordering:menu-snapshot:{restaurantId:N}";

    private IDatabase GetRedisDatabase() =>
        Fixture.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    private Task<RestaurantMenuSnapshotRecord> GetProjectionAsync(Guid restaurantId) =>
        QueryDbAsync(db => db.RestaurantMenuSnapshots
            .AsNoTracking()
            .SingleAsync(x => x.RestaurantId == restaurantId, CancellationToken));
}
