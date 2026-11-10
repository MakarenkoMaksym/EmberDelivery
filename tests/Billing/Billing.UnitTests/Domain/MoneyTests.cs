using EmberDelivery.Billing.Api.Domain;
using EmberDelivery.BuildingBlocks.Domain.Results;

using FluentAssertions;

namespace EmberDelivery.Billing.UnitTests.Domain;

public sealed class MoneyTests
{
    [Fact]
    public void Create_WhenInputIsValid_ShouldNormalizeCurrencyToUpperCase()
    {
        Result<Money> result = Money.Create(120.50m, " uah ");

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(120.50m);
        result.Value.Currency.Should().Be("UAH");
    }

    [Fact]
    public void Create_WhenAmountIsNegative_ShouldFail()
    {
        Result<Money> result = Money.Create(-0.01m, "UAH");

        result.Error.Should().Be(MoneyErrors.NegativeAmount);
    }

    [Fact]
    public void Create_WhenAmountHasSubCentPrecision_ShouldFail()
    {
        Result<Money> result = Money.Create(10.005m, "UAH");

        result.Error.Should().Be(MoneyErrors.TooPrecise);
    }

    [Theory]
    [InlineData("")]
    [InlineData("UA")]
    [InlineData("UAHH")]
    [InlineData("U4H")]
    public void Create_WhenCurrencyIsNotThreeLetterCode_ShouldFail(string currency)
    {
        Result<Money> result = Money.Create(10m, currency);

        result.Error.Should().Be(MoneyErrors.InvalidCurrency);
    }

    [Fact]
    public void Equality_ShouldCompareByAmountAndCurrency()
    {
        Money left = Money.Create(10m, "UAH").Value;
        Money right = Money.Create(10.00m, "uah").Value;

        left.Should().Be(right);
    }
}
