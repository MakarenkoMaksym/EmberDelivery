using EmberDelivery.BuildingBlocks.Domain.Abstractions;

namespace EmberDelivery.Ordering.Domain.ValueObjects;

public readonly record struct Money : IValueObject
{
    public Money(decimal amount, string currency = "UAH")
    {
        if (amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Money amount cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Currency code is required.", nameof(currency));
        }

        // Money never rounds on its own: the business rule that produced a fractional cent must pick the rounding mode.
        if (decimal.Round(amount, 2) != amount)
        {
            throw new ArgumentException("Money amount cannot have more than 2 decimal places.", nameof(amount));
        }

        Amount = amount;
        Currency = currency.Trim().ToUpperInvariant();
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Zero(string currency = "UAH") => new(0m, currency);

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        decimal result = left.Amount - right.Amount;
        return new Money(result < 0m ? 0m : result, left.Currency);
    }

    public static Money operator *(Money money, int multiplier)
    {
        if (multiplier < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(multiplier), "Multiplier cannot be negative.");
        }

        return new Money(money.Amount * multiplier, money.Currency);
    }

    public static bool operator >(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount > right.Amount;
    }

    public static bool operator <(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount < right.Amount;
    }

    public static bool operator >=(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount >= right.Amount;
    }

    public static bool operator <=(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount <= right.Amount;
    }

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (!string.Equals(left.Currency, right.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot perform arithmetic or comparison on different currencies: '{left.Currency}' and '{right.Currency}'.");
        }
    }
}
