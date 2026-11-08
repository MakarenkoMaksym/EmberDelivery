using EmberDelivery.Ordering.Domain.ValueObjects;

using FluentAssertions;

namespace EmberDelivery.Ordering.UnitTests.Domain.ValueObjects;

public sealed class MoneyTests
{
    [Fact]
    public void Constructor_WithValidAmount_ShouldKeepAmountAndNormalizeCurrencyToUppercase()
    {
        // Act
        Money money = new(149.99m, " uah ");

        // Assert
        money.Amount.Should().Be(149.99m);
        money.Currency.Should().Be("UAH");
    }

    [Theory]
    [InlineData(149.996)]
    [InlineData(0.001)]
    public void Constructor_WhenAmountHasMoreThanTwoDecimalPlaces_ShouldThrowArgumentException(double amount)
    {
        // Act
        Action act = () => _ = new Money((decimal)amount);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("amount");
    }

    [Fact]
    public void Constructor_WhenAmountHasInsignificantTrailingZeros_ShouldAcceptIt()
    {
        // Act
        Money money = new(10.500m);

        // Assert
        money.Should().Be(new Money(10.5m));
    }

    [Fact]
    public void SubtractionOperator_WhenSubtrahendExceedsMinuend_ShouldFloorAtZero()
    {
        // Arrange
        Money price = new(100m, "UAH");
        Money discount = new(150m, "UAH");

        // Act
        Money result = price - discount;

        // Assert
        result.Should().Be(Money.Zero("UAH"));
    }

    [Fact]
    public void AdditionOperator_WhenCurrenciesDiffer_ShouldThrowInvalidOperationException()
    {
        // Arrange
        Money uah = new(100m, "UAH");
        Money eur = new(10m, "EUR");

        // Act
        Action act = () => _ = uah + eur;

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_WhenAmountIsNegative_ShouldThrowArgumentOutOfRangeException()
    {
        // Act
        Action act = () => _ = new Money(-0.01m);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
