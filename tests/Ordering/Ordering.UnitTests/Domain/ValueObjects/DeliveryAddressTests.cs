using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.Ordering.Domain.ValueObjects;

using FluentAssertions;

namespace EmberDelivery.Ordering.UnitTests.Domain.ValueObjects;

public sealed class DeliveryAddressTests
{
    [Fact]
    public void Create_WithValidInput_ShouldTrimFieldsAndSupportStructuralEquality()
    {
        // Act
        Result<DeliveryAddress> first = DeliveryAddress.Create(
            street: "  Khreshchatyk  ",
            building: " 22A ",
            latitude: 50.4501,
            longitude: 30.5234,
            apartment: " 15 ",
            deliveryNotes: "   ");

        Result<DeliveryAddress> second = DeliveryAddress.Create(
            street: "Khreshchatyk",
            building: "22A",
            latitude: 50.4501,
            longitude: 30.5234,
            apartment: "15",
            deliveryNotes: null);

        // Assert
        first.IsSuccess.Should().BeTrue();
        first.Value.Street.Should().Be("Khreshchatyk");
        first.Value.Building.Should().Be("22A");
        first.Value.Apartment.Should().Be("15");
        first.Value.DeliveryNotes.Should().BeNull();

        // Value Object invariant: two instances with identical values must be equal.
        first.Value.Should().Be(second.Value);
    }

    [Theory]
    [InlineData(-90.001, 30.0)]
    [InlineData(90.001, 30.0)]
    [InlineData(double.NaN, 30.0)]
    [InlineData(50.0, -180.001)]
    [InlineData(50.0, 180.001)]
    [InlineData(50.0, double.NaN)]
    public void Create_WhenCoordinatesAreOutOfBounds_ShouldReturnValidationFailure(
        double invalidLat,
        double invalidLon)
    {
        // Act
        Result<DeliveryAddress> result = DeliveryAddress.Create(
            "Main St",
            "1",
            invalidLat,
            invalidLon);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Theory]
    [InlineData("", "10")]
    [InlineData("   ", "10")]
    [InlineData("Main St", "")]
    [InlineData("Main St", "   ")]
    public void Create_WhenStreetOrBuildingIsEmpty_ShouldReturnValidationFailure(
        string street,
        string building)
    {
        // Act
        Result<DeliveryAddress> result = DeliveryAddress.Create(
            street,
            building,
            50.4501,
            30.5234);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }
}
