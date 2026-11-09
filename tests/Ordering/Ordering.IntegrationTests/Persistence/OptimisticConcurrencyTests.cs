using System.Net;
using System.Net.Http.Json;

using EmberDelivery.BuildingBlocks.Application.Abstractions;
using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.Ordering.Domain.Aggregates.Coupons;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;
using EmberDelivery.Ordering.Domain.ValueObjects;
using EmberDelivery.Ordering.IntegrationTests.Infrastructure;
using EmberDelivery.Ordering.IntegrationTests.TestData;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

using static EmberDelivery.Ordering.Api.Endpoints.Orders.CancelOrderEndpoint;

namespace EmberDelivery.Ordering.IntegrationTests.Persistence;

// Each "unit of work" below is a separate DI scope, i.e. its own DbContext, exactly like two concurrent requests.
public sealed class OptimisticConcurrencyTests(OrderingAppFixture fixture) : BaseIntegrationTest(fixture)
{
    [Fact]
    public async Task SaveChanges_WhenOrderWasModifiedByAnotherUnitOfWork_ShouldRejectStaleWrite()
    {
        // Arrange
        // Both units of work read the same version of the order.
        Guid orderId = await CreateOrderAsync(await SeedRestaurantMenuAsync());
        uint initialVersion = (await GetOrderFromDbAsync(orderId)).Version;

        await using AsyncServiceScope paymentScope = Fixture.Services.CreateAsyncScope();
        await using AsyncServiceScope cancellationScope = Fixture.Services.CreateAsyncScope();

        Order orderSeenByPayment = await LoadOrderAsync(paymentScope, orderId);
        Order orderSeenByCancellation = await LoadOrderAsync(cancellationScope, orderId);

        orderSeenByPayment.MarkAsPaid().IsSuccess.Should().BeTrue();
        await SaveAsync(paymentScope);

        orderSeenByCancellation.CancelBySystem("Restaurant closed").IsSuccess.Should().BeTrue();

        // Act
        Func<Task> staleSave = () => SaveAsync(cancellationScope);

        // Assert
        await staleSave.Should().ThrowAsync<DbUpdateConcurrencyException>();

        Order persisted = await GetOrderFromDbAsync(orderId);
        persisted.Status.Should().Be(OrderStatus.Paid);
        persisted.CancellationReason.Should().BeNull();
        persisted.Version.Should().Be(initialVersion + 1);
    }

    [Fact]
    public async Task SaveChanges_WhenUnitsOfWorkAreSequential_ShouldBumpPersistedVersionOncePerUnitOfWork()
    {
        // Arrange
        Guid orderId = await CreateOrderAsync(await SeedRestaurantMenuAsync());
        uint initialVersion = (await GetOrderFromDbAsync(orderId)).Version;

        // Act
        await ApplyToOrderAsync(orderId, (order, _) => order.MarkAsPaid());
        await ApplyToOrderAsync(orderId, (order, timeProvider) => order.MarkAsCooking(20, timeProvider));

        // Assert
        Order persisted = await GetOrderFromDbAsync(orderId);
        persisted.Status.Should().Be(OrderStatus.Cooking);
        persisted.Version.Should().Be(initialVersion + 2);
    }

    [Fact]
    public async Task SaveChanges_WhenTwoOrdersRaceForLastCouponRedemption_ShouldLetOnlyOneWin()
    {
        // Arrange
        // A coupon with a single global redemption left, read by two checkouts at the same time.
        Guid restaurantId = await SeedRestaurantMenuAsync();
        Guid couponId = await CreatePercentageCouponAsync("LASTONE", percentage: 10m, maxTotalRedemptions: 1);
        var itemsSubtotal = new Money(TestMenu.StandardBasketSubtotal);

        await using AsyncServiceScope firstCheckout = Fixture.Services.CreateAsyncScope();
        await using AsyncServiceScope secondCheckout = Fixture.Services.CreateAsyncScope();

        Coupon couponSeenByFirst = await LoadCouponAsync(firstCheckout, couponId);
        Coupon couponSeenBySecond = await LoadCouponAsync(secondCheckout, couponId);

        RedeemAndTrack(firstCheckout, couponSeenByFirst, Guid.NewGuid(), restaurantId, itemsSubtotal);
        RedeemAndTrack(secondCheckout, couponSeenBySecond, Guid.NewGuid(), restaurantId, itemsSubtotal);

        await SaveAsync(firstCheckout);

        // Act
        Func<Task> secondSave = () => SaveAsync(secondCheckout);

        // Assert
        await secondSave.Should().ThrowAsync<DbUpdateConcurrencyException>();

        Coupon persisted = await QueryDbAsync(db => db.Coupons.AsNoTracking().SingleAsync(x => x.Id == couponId, CancellationToken));
        persisted.TotalRedemptionsCount.Should().Be(1);
        persisted.Version.Should().Be(2);
        (await QueryDbAsync(db => db.CouponRedemptions.CountAsync(x => x.CouponId == couponId, CancellationToken)))
            .Should().Be(1);
    }

    [Fact]
    public async Task CancelOrder_WhenOrderIsModifiedConcurrently_ShouldReturnConflictAndKeepOrderState()
    {
        // Arrange
        Guid orderId = await CreateOrderAsync(await SeedRestaurantMenuAsync());
        uint initialVersion = (await GetOrderFromDbAsync(orderId)).Version;
        Fixture.Services.GetRequiredService<ConcurrentWriteSimulator>().ArmForNextSaveOf(orderId);

        // Act
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            $"api/v1/orders/{orderId}/cancel",
            new CancelOrderRequest("Too slow"),
            CancellationToken);

        // Assert
        ProblemResponse problem = await response.ShouldBeProblemAsync(HttpStatusCode.Conflict);
        problem.Title.Should().Be("Concurrency Conflict");

        // Only the simulated competing writer's version bump is persisted; the cancellation was rolled back.
        Order persisted = await GetOrderFromDbAsync(orderId);
        persisted.Status.Should().Be(OrderStatus.Submitted);
        persisted.CancellationReason.Should().BeNull();
        persisted.Version.Should().Be(initialVersion + 1);
    }

    private static async Task<Order> LoadOrderAsync(AsyncServiceScope scope, Guid orderId)
    {
        Order? order = await scope.ServiceProvider
            .GetRequiredService<IOrderRepository>()
            .GetByIdAsync(orderId, CancellationToken);

        return order!;
    }

    private static async Task<Coupon> LoadCouponAsync(AsyncServiceScope scope, Guid couponId)
    {
        Coupon? coupon = await scope.ServiceProvider
            .GetRequiredService<ICouponRepository>()
            .GetByIdAsync(couponId, CancellationToken);

        return coupon!;
    }

    private static void RedeemAndTrack(
        AsyncServiceScope scope,
        Coupon coupon,
        Guid orderId,
        Guid restaurantId,
        Money itemsSubtotal)
    {
        Result<CouponRedemption> redemption = coupon.Redeem(
            customerId: Guid.NewGuid(),
            orderId,
            restaurantId,
            itemsSubtotal,
            customerPreviousRedemptions: 0,
            TimeProvider.System);

        redemption.IsSuccess.Should().BeTrue();
        scope.ServiceProvider.GetRequiredService<ICouponRepository>().AddRedemption(redemption.Value);
    }

    private static Task SaveAsync(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken);
}
