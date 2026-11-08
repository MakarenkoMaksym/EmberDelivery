using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;
using EmberDelivery.Ordering.Domain.ValueObjects;

using FluentAssertions;

namespace EmberDelivery.Ordering.UnitTests.Domain.Aggregates.Orders;

public sealed class OrderLineTests
{
    [Fact]
    public void Create_WithValidItemAndModifiers_ShouldCalculateLineTotalAsUnitPlusModifiersTimesQuantity()
    {
        // Arrange: Base price 150 UAH + modifiers (25 UAH + 15 UAH) = 190 UAH per unit * 3 = 570 UAH.
        SelectedModifier cheeseCrust = new(Guid.NewGuid(), "Cheese Crust", new Money(25m));
        SelectedModifier jalapeno = new(Guid.NewGuid(), "Extra Jalapeno", new Money(15m));

        // Act
        Result<OrderLine> result = OrderLine.Create(
            menuItemId: Guid.NewGuid(),
            name: "  Pepperoni Pizza  ",
            baseUnitPrice: new Money(150m),
            quantity: 3,
            modifiers: [cheeseCrust, jalapeno]);

        // Assert
        result.IsSuccess.Should().BeTrue();
        OrderLine line = result.Value;

        line.Name.Should().Be("Pepperoni Pizza");
        line.Quantity.Should().Be(3);
        line.Modifiers.Should().HaveCount(2);
        line.LineTotal.Should().Be(new Money(570m));
    }

    [Fact]
    public void Create_WhenDuplicateModifierIdsAreProvided_ShouldReturnDuplicateModifierFailure()
    {
        // Arrange
        var duplicateModifierId = Guid.NewGuid();
        SelectedModifier firstInstance = new(duplicateModifierId, "Cheese Sauce", new Money(20m));
        SelectedModifier secondInstance = new(duplicateModifierId, "Cheese Sauce", new Money(20m));

        // Act
        Result<OrderLine> result = OrderLine.Create(
            menuItemId: Guid.NewGuid(),
            name: "French Fries",
            baseUnitPrice: new Money(80m),
            quantity: 1,
            modifiers: [firstInstance, secondInstance]);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Order.DuplicateModifier");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-50)]
    public void Create_WhenQuantityIsZeroOrNegative_ShouldReturnInvalidItemQuantityFailure(int invalidQuantity)
    {
        // Act
        Result<OrderLine> result = OrderLine.Create(
            menuItemId: Guid.NewGuid(),
            name: "Burger",
            baseUnitPrice: new Money(120m),
            quantity: invalidQuantity,
            modifiers: []);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Order.InvalidItemQuantity");
    }

    [Fact]
    public void Create_WhenMenuItemIdIsEmpty_ShouldReturnInvalidMenuItemIdFailure()
    {
        // Act
        Result<OrderLine> result = OrderLine.Create(
            menuItemId: Guid.Empty,
            name: "Burger",
            baseUnitPrice: new Money(120m),
            quantity: 1,
            modifiers: []);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.InvalidMenuItemId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenNameIsWhitespace_ShouldReturnEmptyMenuItemNameFailure(string invalidName)
    {
        // Act
        Result<OrderLine> result = OrderLine.Create(
            menuItemId: Guid.NewGuid(),
            name: invalidName,
            baseUnitPrice: new Money(120m),
            quantity: 1,
            modifiers: []);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.EmptyMenuItemName);
    }
}
