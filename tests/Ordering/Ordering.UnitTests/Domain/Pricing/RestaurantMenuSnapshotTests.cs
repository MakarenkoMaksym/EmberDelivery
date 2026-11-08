using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;
using EmberDelivery.Ordering.Domain.Pricing;
using EmberDelivery.Ordering.Domain.ValueObjects;

using FluentAssertions;

namespace EmberDelivery.Ordering.UnitTests.Domain.Pricing;

public sealed class RestaurantMenuSnapshotTests
{
    private static readonly Guid AvailableItemId = Guid.NewGuid();
    private static readonly Guid StopListedItemId = Guid.NewGuid();
    private static readonly Guid AvailableModifierId = Guid.NewGuid();
    private static readonly Guid StopListedModifierId = Guid.NewGuid();

    [Fact]
    public void CreateOrderLines_WhenAllItemsAndModifiersAreAvailable_ShouldReturnValidatedOrderLines()
    {
        // Arrange
        RestaurantMenuSnapshot snapshot = CreateSampleSnapshot();
        OrderLineSpec spec = new(
            AvailableItemId,
            Quantity: 2,
            SelectedModifierIds: [AvailableModifierId]);

        // Act
        Result<IReadOnlyList<OrderLine>> result = snapshot.CreateOrderLines([spec]);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();

        OrderLine line = result.Value[0];
        line.MenuItemId.Should().Be(AvailableItemId);
        line.LineTotal.Should().Be(new Money(460m)); // (200 base + 30 mod) * 2
    }

    [Fact]
    public void CreateOrderLines_WhenRequestedItemIsInStopList_ShouldReturnMenuItemUnavailableFailure()
    {
        // Arrange
        RestaurantMenuSnapshot snapshot = CreateSampleSnapshot();
        OrderLineSpec spec = new(
            StopListedItemId,
            Quantity: 1,
            SelectedModifierIds: []);

        // Act
        Result<IReadOnlyList<OrderLine>> result = snapshot.CreateOrderLines([spec]);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Order.MenuItemUnavailable");
    }

    [Fact]
    public void CreateOrderLines_WhenRequestedModifierIsInStopListOrNotAllowed_ShouldReturnModifierUnavailableFailure()
    {
        // Arrange
        RestaurantMenuSnapshot snapshot = CreateSampleSnapshot();
        OrderLineSpec spec = new(
            AvailableItemId,
            Quantity: 1,
            SelectedModifierIds: [StopListedModifierId]);

        // Act
        Result<IReadOnlyList<OrderLine>> result = snapshot.CreateOrderLines([spec]);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Order.ModifierUnavailable");
    }

    [Fact]
    public void CreateOrderLines_WhenSpecsCollectionIsEmpty_ShouldReturnEmptyBasketFailure()
    {
        // Arrange
        RestaurantMenuSnapshot snapshot = CreateSampleSnapshot();

        // Act
        Result<IReadOnlyList<OrderLine>> result = snapshot.CreateOrderLines([]);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.EmptyBasket);
    }

    private static RestaurantMenuSnapshot CreateSampleSnapshot()
    {
        Dictionary<Guid, MenuModifierSnapshot> modifiers = new()
        {
            [AvailableModifierId] = new MenuModifierSnapshot(
                AvailableModifierId,
                "Double Cheese",
                new Money(30m),
                IsAvailable: true),
            [StopListedModifierId] = new MenuModifierSnapshot(
                StopListedModifierId,
                "Truffle Mayo",
                new Money(45m),
                IsAvailable: false)
        };

        Dictionary<Guid, MenuItemSnapshot> items = new()
        {
            [AvailableItemId] = new MenuItemSnapshot(
                AvailableItemId,
                "Classic Burger",
                new Money(200m),
                IsAvailable: true,
                modifiers),
            [StopListedItemId] = new MenuItemSnapshot(
                StopListedItemId,
                "Salmon Bowl",
                new Money(320m),
                IsAvailable: false,
                new Dictionary<Guid, MenuModifierSnapshot>())
        };

        return new RestaurantMenuSnapshot(
            restaurantId: Guid.NewGuid(),
            isAcceptingOrders: true,
            minimumBasketRequirement: new Money(250m),
            baseDeliveryFee: new Money(50m),
            freeDeliveryThreshold: new Money(600m),
            items: items);
    }
}
