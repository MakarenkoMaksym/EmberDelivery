using EmberDelivery.BuildingBlocks.Domain.Abstractions;
using EmberDelivery.BuildingBlocks.Domain.Results;

namespace EmberDelivery.Billing.Api.Domain;

// Billing owns its Money instead of sharing Ordering's: the contexts must evolve monetary rules (sign, precision,
// supported currencies) independently, and a shared assembly would couple their business logic and release cycles.
public readonly record struct Money : IValueObject
{
    private const int CurrencyCodeLength = 3;

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public bool IsPositive => Amount > 0m;

    // Values arrive from untrusted message payloads, so construction reports failures as Results that a consumer can
    // turn into a deterministic rejection instead of an exception that would bounce the message to the error queue.
    public static Result<Money> Create(decimal amount, string currency)
    {
        if (amount < 0m)
        {
            return Result.Failure<Money>(MoneyErrors.NegativeAmount);
        }

        // Gateways settle in minor units; sub-cent precision would be silently truncated by the provider.
        if (decimal.Round(amount, 2) != amount)
        {
            return Result.Failure<Money>(MoneyErrors.TooPrecise);
        }

        string normalizedCurrency = currency?.Trim() ?? string.Empty;

        if (normalizedCurrency.Length != CurrencyCodeLength || !normalizedCurrency.All(char.IsAsciiLetter))
        {
            return Result.Failure<Money>(MoneyErrors.InvalidCurrency);
        }

        return Result.Success(new Money(amount, normalizedCurrency.ToUpperInvariant()));
    }
}
