using EmberDelivery.Ordering.Domain.Pricing;
using EmberDelivery.Ordering.Domain.ValueObjects;

using FluentAssertions;

namespace EmberDelivery.Ordering.UnitTests.Domain.Pricing;

public sealed class DeliveryPricingPolicyTests
{
    [Fact]
    public void CalculateFee_WhenSubtotalIsBelowFreeDeliveryThreshold_ShouldReturnBaseFeeMultipliedBySurge()
    {
        // Arrange: Base fee 50 UAH * 1.5 surge = 75 UAH.
        DeliveryPricingPolicy policy = new(
            baseFee: new Money(50m),
            surgeMultiplier: 1.5m,
            freeDeliveryThreshold: new Money(500m));

        // Act
        Money fee = policy.CalculateFee(preDiscountItemsSubtotal: new Money(350m));

        // Assert
        fee.Should().Be(new Money(75m));
    }

    [Fact]
    public void CalculateFee_WhenSurgeProducesHalfCent_ShouldRoundAwayFromZero()
    {
        // Arrange: 10.02 UAH * 1.25 = 12.525 UAH, a midpoint that banker's rounding would send down to 12.52.
        DeliveryPricingPolicy policy = new(
            baseFee: new Money(10.02m),
            surgeMultiplier: 1.25m);

        // Act
        Money fee = policy.CalculateFee(preDiscountItemsSubtotal: new Money(350m));

        // Assert
        fee.Should().Be(new Money(12.53m));
    }

    [Fact]
    public void CalculateFee_WhenSubtotalMeetsThresholdAndNoSurgeIsActive_ShouldReturnZeroFee()
    {
        // Arrange
        DeliveryPricingPolicy policy = new(
            baseFee: new Money(50m),
            surgeMultiplier: 1.0m,
            freeDeliveryThreshold: new Money(400m));

        // Act
        Money fee = policy.CalculateFee(preDiscountItemsSubtotal: new Money(400m));

        // Assert
        fee.Should().Be(Money.Zero());
    }

    [Fact]
    public void CalculateFee_WhenSubtotalMeetsThresholdButSurgeIsActive_ShouldSuspendFreeDeliveryAndChargeSurgedFee()
    {
        // Arrange: Subtotal is 600 UAH (>= 400 UAH threshold), but 1.2x rain surge is active.
        DeliveryPricingPolicy policy = new(
            baseFee: new Money(50m),
            surgeMultiplier: 1.2m,
            freeDeliveryThreshold: new Money(400m));

        // Act
        Money fee = policy.CalculateFee(preDiscountItemsSubtotal: new Money(600m));

        // Assert
        fee.Should().Be(new Money(60m));
    }

    [Theory]
    [InlineData(0.99)]
    [InlineData(0.0)]
    [InlineData(5.01)]
    public void Constructor_WhenSurgeMultiplierIsOutOfAllowedRange_ShouldThrowArgumentOutOfRangeException(decimal invalidSurge)
    {
        // Act
        Action act = () => _ = new DeliveryPricingPolicy(
            baseFee: new Money(50m),
            surgeMultiplier: invalidSurge);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
