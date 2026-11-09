using System.Net;
using System.Net.Http.Json;

using EmberDelivery.Ordering.Domain.Aggregates.Coupons;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;
using EmberDelivery.Ordering.IntegrationTests.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

using static EmberDelivery.Ordering.Api.Endpoints.Orders.CancelOrderEndpoint;

namespace EmberDelivery.Ordering.IntegrationTests.Endpoints.Orders;

public sealed class CancelOrderEndpointTests(OrderingAppFixture fixture) : BaseIntegrationTest(fixture)
{
    [Fact]
    public async Task CancelOrder_WhenOrderIsSubmitted_ShouldReturnNoContentAndCancelOrder()
    {
        // Arrange
        Guid restaurantId = await SeedRestaurantMenuAsync();
        Guid orderId = await CreateOrderAsync(restaurantId);
        uint initialVersion = (await GetOrderFromDbAsync(orderId)).Version;

        // Act
        HttpResponseMessage response = await CancelAsync(HttpClient, orderId, "  Changed my mind  ");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        Order order = await GetOrderFromDbAsync(orderId);
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be("Changed my mind");
        order.Version.Should().Be(initialVersion + 1);
    }

    [Fact]
    public async Task CancelOrder_WhenOrderUsedCoupon_ShouldReleaseRedemptionInSameTransaction()
    {
        // Arrange
        Guid restaurantId = await SeedRestaurantMenuAsync();
        Guid couponId = await CreatePercentageCouponAsync("COMEBACK10", percentage: 10m);
        Guid orderId = await CreateOrderAsync(restaurantId, couponCode: "COMEBACK10", expectedTotalAmount: 365m);

        // Act
        HttpResponseMessage response = await CancelAsync(HttpClient, orderId, reason: null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        Coupon coupon = await QueryDbAsync(db => db.Coupons.SingleAsync(x => x.Id == couponId, CancellationToken));
        coupon.TotalRedemptionsCount.Should().Be(0);

        bool redemptionExists = await QueryDbAsync(db => db.CouponRedemptions
            .AnyAsync(x => x.OrderId == orderId, CancellationToken));
        redemptionExists.Should().BeFalse();
    }

    [Fact]
    public async Task CancelOrder_WhenOrderIsAlreadyCancelled_ShouldReturnNoContentWithoutBumpingVersion()
    {
        // Arrange
        Guid restaurantId = await SeedRestaurantMenuAsync();
        Guid orderId = await CreateOrderAsync(restaurantId);
        (await CancelAsync(HttpClient, orderId, "First")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        uint versionAfterFirstCancel = (await GetOrderFromDbAsync(orderId)).Version;

        // Act
        HttpResponseMessage response = await CancelAsync(HttpClient, orderId, "Second");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        Order order = await GetOrderFromDbAsync(orderId);
        order.CancellationReason.Should().Be("First");
        order.Version.Should().Be(versionAfterFirstCancel);
    }

    [Fact]
    public async Task CancelOrder_WhenKitchenAlreadyStartedCooking_ShouldReturnConflict()
    {
        // Arrange
        Guid restaurantId = await SeedRestaurantMenuAsync();
        Guid orderId = await CreateOrderAsync(restaurantId);
        await MoveOrderToCookingAsync(orderId);

        // Act
        HttpResponseMessage response = await CancelAsync(HttpClient, orderId, reason: null);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "Order.CannotCancelInProgressOrder");
        (await GetOrderFromDbAsync(orderId)).Status.Should().Be(OrderStatus.Cooking);
    }

    [Fact]
    public async Task CancelOrder_WhenOrderBelongsToAnotherCustomer_ShouldReturnForbiddenAndKeepOrder()
    {
        // Arrange
        Guid restaurantId = await SeedRestaurantMenuAsync();
        Guid orderId = await CreateOrderAsync(restaurantId);
        using HttpClient strangerClient = CreateClientFor(Guid.NewGuid(), TestRoles.Customer);

        // Act
        HttpResponseMessage response = await CancelAsync(strangerClient, orderId, reason: null);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "Order.ForbiddenCancellation");
        (await GetOrderFromDbAsync(orderId)).Status.Should().Be(OrderStatus.Submitted);
    }

    [Fact]
    public async Task CancelOrder_WhenOrderDoesNotExist_ShouldReturnNotFound()
    {
        // Act
        HttpResponseMessage response = await CancelAsync(HttpClient, Guid.NewGuid(), reason: null);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "Order.NotFound");
    }

    private static Task<HttpResponseMessage> CancelAsync(HttpClient client, Guid orderId, string? reason) =>
        client.PostAsJsonAsync($"api/v1/orders/{orderId}/cancel", new CancelOrderRequest(reason), CancellationToken);
}
