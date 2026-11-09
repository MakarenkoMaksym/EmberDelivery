using EmberDelivery.Ordering.Domain.Aggregates.Coupons;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;
using EmberDelivery.Ordering.IntegrationTests.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

using static EmberDelivery.Ordering.IntegrationEvents.OrderSagaLifecycleEvents;

namespace EmberDelivery.Ordering.IntegrationTests.Consumers;

public sealed class OrderSagaLifecycleConsumerTests(OrderingAppFixture fixture) : BaseIntegrationTest(fixture)
{
    private static readonly Guid CourierId = Guid.Parse("c0c0c0c0-0000-4000-8000-000000000001");

    [Fact]
    public async Task Consume_OrderPaymentConfirmed_ShouldMarkSubmittedOrderAsPaid()
    {
        // Arrange
        Guid orderId = await CreateOrderAsync(await SeedRestaurantMenuAsync());
        uint initialVersion = (await GetOrderFromDbAsync(orderId)).Version;

        // Act
        await PublishAndWaitForConsumptionAsync(new OrderPaymentConfirmedIntegrationEvent(orderId));

        // Assert
        Order order = await GetOrderFromDbAsync(orderId);
        order.Status.Should().Be(OrderStatus.Paid);
        order.Version.Should().Be(initialVersion + 1);
    }

    [Fact]
    public async Task Consume_OrderCookingStarted_ShouldMarkPaidOrderAsCookingWithDeliveryEstimate()
    {
        // Arrange
        Guid orderId = await CreateOrderAsync(await SeedRestaurantMenuAsync());
        await ApplyToOrderAsync(orderId, (order, _) => order.MarkAsPaid());
        uint initialVersion = (await GetOrderFromDbAsync(orderId)).Version;

        // Act
        DateTimeOffset publishedAtUtc = DateTimeOffset.UtcNow;
        await PublishAndWaitForConsumptionAsync(new OrderCookingStartedIntegrationEvent(orderId, EstimatedPrepMinutes: 25));

        // Assert
        // Estimate = prep time + 15 minutes of travel, measured from the moment the event was handled.
        Order order = await GetOrderFromDbAsync(orderId);
        order.Status.Should().Be(OrderStatus.Cooking);
        order.EstimatedDeliveryAtUtc.Should().BeCloseTo(publishedAtUtc.AddMinutes(40), TimeSpan.FromMinutes(1));
        order.Version.Should().Be(initialVersion + 1);
    }

    [Fact]
    public async Task Consume_OrderDeliveryStarted_ShouldAssignCourierToCookingOrder()
    {
        // Arrange
        Guid orderId = await CreateOrderAsync(await SeedRestaurantMenuAsync());
        await MoveOrderToCookingAsync(orderId);
        uint initialVersion = (await GetOrderFromDbAsync(orderId)).Version;

        // Act
        await PublishAndWaitForConsumptionAsync(new OrderDeliveryStartedIntegrationEvent(orderId, CourierId));

        // Assert
        Order order = await GetOrderFromDbAsync(orderId);
        order.Status.Should().Be(OrderStatus.Delivering);
        order.AssignedCourierId.Should().Be(CourierId);
        order.Version.Should().Be(initialVersion + 1);
    }

    [Fact]
    public async Task Consume_OrderFulfillmentCompleted_ShouldCompleteDeliveringOrder()
    {
        // Arrange
        Guid orderId = await CreateOrderAsync(await SeedRestaurantMenuAsync());
        await MoveOrderToCookingAsync(orderId);
        await ApplyToOrderAsync(orderId, (order, _) => order.MarkAsDelivering(CourierId));
        uint initialVersion = (await GetOrderFromDbAsync(orderId)).Version;

        // Act
        await PublishAndWaitForConsumptionAsync(new OrderFulfillmentCompletedIntegrationEvent(orderId, CourierId));

        // Assert
        Order order = await GetOrderFromDbAsync(orderId);
        order.Status.Should().Be(OrderStatus.Completed);
        order.CompletedAtUtc.Should().NotBeNull();
        order.Version.Should().Be(initialVersion + 1);
    }

    [Fact]
    public async Task Consume_OrderFulfillmentFailed_ShouldCancelOrderAndReleaseCoupon()
    {
        // Arrange
        Guid restaurantId = await SeedRestaurantMenuAsync();
        Guid couponId = await CreatePercentageCouponAsync("FAILSAFE10", percentage: 10m);
        Guid orderId = await CreateOrderAsync(restaurantId, couponCode: "FAILSAFE10", expectedTotalAmount: 365m);
        uint initialVersion = (await GetOrderFromDbAsync(orderId)).Version;

        // Act
        await PublishAndWaitForConsumptionAsync(new OrderFulfillmentFailedIntegrationEvent(orderId, "Card declined"));

        // Assert
        Order order = await GetOrderFromDbAsync(orderId);
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be("Card declined");
        order.Version.Should().Be(initialVersion + 1);

        Coupon coupon = await QueryDbAsync(db => db.Coupons.SingleAsync(x => x.Id == couponId, CancellationToken));
        coupon.TotalRedemptionsCount.Should().Be(0);
        (await QueryDbAsync(db => db.CouponRedemptions.AnyAsync(x => x.OrderId == orderId, CancellationToken)))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Consume_OrderPaymentConfirmedDeliveredTwice_ShouldApplyTransitionOnlyOnce()
    {
        // Arrange

        // Brokers guarantee at-least-once delivery, so a redelivered event must not bump the version again.
        Guid orderId = await CreateOrderAsync(await SeedRestaurantMenuAsync());
        uint initialVersion = (await GetOrderFromDbAsync(orderId)).Version;
        await PublishAndWaitForConsumptionAsync(new OrderPaymentConfirmedIntegrationEvent(orderId));

        // Act
        await PublishAndWaitForConsumptionAsync(new OrderPaymentConfirmedIntegrationEvent(orderId));

        // Assert
        Order order = await GetOrderFromDbAsync(orderId);
        order.Status.Should().Be(OrderStatus.Paid);
        order.Version.Should().Be(initialVersion + 1);
    }

    [Fact]
    public async Task Consume_OrderDeliveryStartedForSubmittedOrder_ShouldLeaveOrderUntouched()
    {
        // Arrange
        Guid orderId = await CreateOrderAsync(await SeedRestaurantMenuAsync());
        uint initialVersion = (await GetOrderFromDbAsync(orderId)).Version;

        // Act
        await PublishAndWaitForConsumptionAsync(new OrderDeliveryStartedIntegrationEvent(orderId, CourierId));

        // Assert
        Order order = await GetOrderFromDbAsync(orderId);
        order.Status.Should().Be(OrderStatus.Submitted);
        order.AssignedCourierId.Should().BeNull();
        order.Version.Should().Be(initialVersion);
    }
}
