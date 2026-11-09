using System.Net;
using System.Net.Http.Json;

using EmberDelivery.Ordering.Application.Orders.Commands.CreateOrder;
using EmberDelivery.Ordering.Domain.Aggregates.Coupons;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;
using EmberDelivery.Ordering.Domain.ValueObjects;
using EmberDelivery.Ordering.IntegrationTests.Infrastructure;
using EmberDelivery.Ordering.IntegrationTests.TestData;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

using static EmberDelivery.Ordering.Api.Endpoints.Orders.CreateOrderEndpoint;

namespace EmberDelivery.Ordering.IntegrationTests.Endpoints.Orders;

public sealed class CreateOrderEndpointTests(OrderingAppFixture fixture) : BaseIntegrationTest(fixture)
{
    private const string OrdersUrl = "api/v1/orders";

    [Fact]
    public async Task CreateOrder_InEuroWithModifier_ShouldRoundTripEuroOnOrderLineAndModifierThroughDatabase()
    {
        // Arrange
        Guid restaurantId = await SeedRestaurantMenuAsync(currency: "EUR");

        // Act
        Guid orderId = await CreateOrderAsync(restaurantId, currency: "EUR");

        // Assert
        // A fresh, non-tracking DbContext guarantees every Money is materialized from PostgreSQL columns.
        Order order = await QueryDbAsync(db => db.Orders
            .AsNoTracking()
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == orderId, CancellationToken));

        order.ItemsSubtotal.Should().Be(new Money(TestMenu.StandardBasketSubtotal, "EUR"));
        order.TotalPrice.Should().Be(new Money(TestMenu.StandardBasketTotal, "EUR"));

        OrderLine line = order.Lines.Should().ContainSingle().Subject;
        line.BaseUnitPrice.Should().Be(new Money(TestMenu.BurgerPrice, "EUR"));
        line.LineTotal.Currency.Should().Be("EUR");

        SelectedModifier modifier = line.Modifiers.Should().ContainSingle().Subject;
        modifier.ModifierId.Should().Be(TestMenu.CheeseId);
        modifier.Price.Should().Be(new Money(TestMenu.CheesePrice, "EUR"));
    }

    [Fact]
    public async Task CreateOrder_WithValidBasket_ShouldReturnCreatedAndPersistSubmittedOrder()
    {
        // Arrange
        Guid restaurantId = await SeedRestaurantMenuAsync();

        // Act
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            OrdersUrl,
            TestMenu.StandardOrderRequest(restaurantId),
            CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        CreateOrderResponse? body = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(CancellationToken);
        response.Headers.Location!.OriginalString.Should().Be($"/api/v1/orders/{body!.Id}");

        Order order = await QueryDbAsync(db => db.Orders
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == body.Id, CancellationToken));

        order.CustomerId.Should().Be(CustomerId);
        order.RestaurantId.Should().Be(restaurantId);
        order.Status.Should().Be(OrderStatus.Submitted);
        order.ItemsSubtotal.Amount.Should().Be(350m);
        order.DeliveryFee.Amount.Should().Be(50m);
        order.DiscountAmount.Amount.Should().Be(0m);
        order.TotalPrice.Amount.Should().Be(400m);
        order.TotalPrice.Currency.Should().Be("UAH");
        order.DeliveryAddress.Street.Should().Be("Khreshchatyk");

        OrderLine line = order.Lines.Should().ContainSingle().Subject;
        line.MenuItemId.Should().Be(TestMenu.BurgerId);
        line.Quantity.Should().Be(2);
        line.LineTotal.Amount.Should().Be(350m);
        line.Modifiers.Should().ContainSingle(m => m.ModifierId == TestMenu.CheeseId && m.Price.Amount == 25m);
    }

    [Fact]
    public async Task CreateOrder_WithPercentageCoupon_ShouldApplyDiscountAndRecordRedemption()
    {
        // Arrange
        // 10% of 350 = 35 discount, so 350 - 35 + 50 delivery = 365.
        Guid restaurantId = await SeedRestaurantMenuAsync();
        Guid couponId = await CreatePercentageCouponAsync("SPRING10", percentage: 10m);

        // Act
        Guid orderId = await CreateOrderAsync(restaurantId, couponCode: "spring10", expectedTotalAmount: 365m);

        // Assert
        Order order = await QueryDbAsync(db => db.Orders.SingleAsync(x => x.Id == orderId, CancellationToken));
        order.DiscountAmount.Amount.Should().Be(35m);
        order.TotalPrice.Amount.Should().Be(365m);
        order.AppliedCouponId.Should().Be(couponId);
        order.AppliedCouponCode.Should().Be("SPRING10");

        Coupon coupon = await QueryDbAsync(db => db.Coupons.SingleAsync(x => x.Id == couponId, CancellationToken));
        coupon.TotalRedemptionsCount.Should().Be(1);

        CouponRedemption redemption = await QueryDbAsync(db => db.CouponRedemptions
            .SingleAsync(x => x.CouponId == couponId, CancellationToken));
        redemption.OrderId.Should().Be(orderId);
        redemption.CustomerId.Should().Be(CustomerId);
        redemption.DiscountApplied.Amount.Should().Be(35m);
    }

    [Fact]
    public async Task CreateOrder_WhenCustomerAlreadyUsedSingleUseCoupon_ShouldReturnConflict()
    {
        // Arrange
        Guid restaurantId = await SeedRestaurantMenuAsync();
        await CreatePercentageCouponAsync("ONCE10", percentage: 10m);
        await CreateOrderAsync(restaurantId, couponCode: "ONCE10", expectedTotalAmount: 365m);

        // Act
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            OrdersUrl,
            TestMenu.StandardOrderRequest(restaurantId, expectedTotalAmount: 365m, couponCode: "ONCE10"),
            CancellationToken);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "Coupon.CustomerUsageLimitReached");
        (await CountOrdersAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateOrder_WhenExpectedTotalIsStale_ShouldReturnConflictAndNotPersistOrder()
    {
        // Arrange
        Guid restaurantId = await SeedRestaurantMenuAsync();

        // Act
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            OrdersUrl,
            TestMenu.StandardOrderRequest(restaurantId, expectedTotalAmount: 380m),
            CancellationToken);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "Order.PriceChanged");
        (await CountOrdersAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateOrder_WhenRestaurantIsNotAcceptingOrders_ShouldReturnConflict()
    {
        // Arrange
        Guid restaurantId = await SeedRestaurantMenuAsync(isAcceptingOrders: false);

        // Act
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            OrdersUrl,
            TestMenu.StandardOrderRequest(restaurantId),
            CancellationToken);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "Order.RestaurantUnavailable");
    }

    [Fact]
    public async Task CreateOrder_WhenRestaurantMenuIsUnknown_ShouldReturnConflict()
    {
        // Act
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            OrdersUrl,
            TestMenu.StandardOrderRequest(restaurantId: Guid.NewGuid()),
            CancellationToken);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "Order.RestaurantUnavailable");
    }

    [Fact]
    public async Task CreateOrder_WhenMenuItemIsSoldOut_ShouldReturnConflict()
    {
        // Arrange
        Guid restaurantId = await SeedRestaurantMenuAsync();
        CreateOrderRequest request = TestMenu.StandardOrderRequest(restaurantId) with
        {
            Items = [new OrderItemRequest(TestMenu.SoldOutSoupId, Quantity: 3, SelectedModifierIds: [])]
        };

        // Act
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(OrdersUrl, request, CancellationToken);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "Order.MenuItemUnavailable");
    }

    [Fact]
    public async Task CreateOrder_WhenCouponDoesNotExist_ShouldReturnNotFound()
    {
        // Arrange
        Guid restaurantId = await SeedRestaurantMenuAsync();

        // Act
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            OrdersUrl,
            TestMenu.StandardOrderRequest(restaurantId, couponCode: "GHOST"),
            CancellationToken);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "Order.CouponNotFound");
    }

    [Fact]
    public async Task CreateOrder_WithMalformedRequest_ShouldReturnBadRequestWithValidationErrors()
    {
        // Arrange
        Guid restaurantId = await SeedRestaurantMenuAsync();
        CreateOrderRequest request = TestMenu.StandardOrderRequest(restaurantId, expectedTotalAmount: 400.005m) with
        {
            Items = []
        };

        // Act
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(OrdersUrl, request, CancellationToken);

        // Assert
        ProblemResponse problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        problem.Errors!.Select(e => e.Code).Should().BeEquivalentTo("Items", "ExpectedTotalAmount");
    }

    [Fact]
    public async Task CreateOrder_WhenUnauthenticated_ShouldReturnUnauthorized()
    {
        // Arrange
        Guid restaurantId = await SeedRestaurantMenuAsync();
        ClearAuthentication();

        // Act
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            OrdersUrl,
            TestMenu.StandardOrderRequest(restaurantId),
            CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private Task<int> CountOrdersAsync() => QueryDbAsync(db => db.Orders.CountAsync(CancellationToken));
}
