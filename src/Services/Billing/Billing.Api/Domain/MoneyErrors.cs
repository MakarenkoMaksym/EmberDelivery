using EmberDelivery.BuildingBlocks.Domain.Results;

namespace EmberDelivery.Billing.Api.Domain;

public static class MoneyErrors
{
    public static readonly Error NegativeAmount = Error.Validation(
        "Money.NegativeAmount",
        "Money amount cannot be negative.");

    public static readonly Error TooPrecise = Error.Validation(
        "Money.TooPrecise",
        "Money amount cannot have more than 2 decimal places.");

    public static readonly Error InvalidCurrency = Error.Validation(
        "Money.InvalidCurrency",
        "Currency must be a 3-letter ISO 4217 code.");
}
