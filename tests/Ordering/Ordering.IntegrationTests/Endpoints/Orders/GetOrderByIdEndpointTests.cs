using System.Net;
using System.Net.Http.Json;

using EmberDelivery.Ordering.Application.Orders.Queries.GetOrderById;
using EmberDelivery.Ordering.IntegrationTests.Infrastructure;
using EmberDelivery.Ordering.IntegrationTests.TestData;

using FluentAssertions;

using Xunit;

namespace EmberDelivery.Ordering.IntegrationTests.Endpoints.Orders;

public sealed class GetOrderByIdEndpointTests(OrderingAppFixture fixture) : BaseIntegrationTest(fixture)
{
    [Fact]
    public async Task GetOrderById_WhenOrderBelongsToCaller_ShouldReturnFullOrderProjection()
    {
        // Arrange
        Guid restaurantId = await SeedRestaurantMenuAsync();
        Guid orderId = await CreateOrderAsync(restaurantId);

        // Act
        HttpResponseMessage response = await HttpClient.GetAsync($"api/v1/orders/{orderId}", CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        OrderResponse? order = await response.Content.ReadFromJsonAsync<OrderResponse>(CancellationToken);

        order!.Id.Should().Be(orderId);
        order.CustomerId.Should().Be(CustomerId);
        order.RestaurantId.Should().Be(restaurantId);
        order.Status.Should().Be("Submitted");
        order.Street.Should().Be("Khreshchatyk");
        order.Building.Should().Be("22");
        order.ItemsSubtotal.Should().Be(350m);
        order.DeliveryFee.Should().Be(50m);
        order.DiscountAmount.Should().Be(0m);
        order.TotalPrice.Should().Be(400m);
        order.Currency.Should().Be("UAH");
        order.AppliedCouponCode.Should().BeNull();

        OrderLineResponse line = order.Lines.Should().ContainSingle().Subject;
        line.Name.Should().Be("Ember Burger");
        line.BaseUnitPrice.Should().Be(150m);
        line.Quantity.Should().Be(2);
        line.LineTotal.Should().Be(350m);
        line.Modifiers.Should().ContainSingle()
            .Which.Should().Be(new OrderModifierResponse(TestMenu.CheeseId, "Cheddar", 25m));
    }

    [Fact]
    public async Task GetOrderById_WhenOrderBelongsToAnotherCustomer_ShouldReturnForbidden()
    {
        // Arrange
        Guid restaurantId = await SeedRestaurantMenuAsync();
        Guid orderId = await CreateOrderAsync(restaurantId);
        using HttpClient strangerClient = CreateClientFor(Guid.NewGuid(), TestRoles.Customer);

        // Act
        HttpResponseMessage response = await strangerClient.GetAsync($"api/v1/orders/{orderId}", CancellationToken);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "Order.ForbiddenAccess");
    }

    [Fact]
    public async Task GetOrderById_WhenOrderDoesNotExist_ShouldReturnNotFound()
    {
        // Act
        HttpResponseMessage response = await HttpClient.GetAsync($"api/v1/orders/{Guid.NewGuid()}", CancellationToken);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "Order.NotFound");
    }
}
