using EmberDelivery.BuildingBlocks.Domain.Abstractions;
using EmberDelivery.Ordering.Domain.ValueObjects;

namespace EmberDelivery.Ordering.Domain.Pricing;

public sealed record DeliveryPricingPolicy : IValueObject
{
    private const decimal MinSurgeMultiplier = 1.0m;
    private const decimal MaxSurgeMultiplier = 5.0m;

    public DeliveryPricingPolicy(
        Money baseFee,
        decimal surgeMultiplier = MinSurgeMultiplier,
        Money? freeDeliveryThreshold = null)
    {
        if (surgeMultiplier < MinSurgeMultiplier || surgeMultiplier > MaxSurgeMultiplier)
        {
            throw new ArgumentOutOfRangeException(
                nameof(surgeMultiplier),
                $"Surge multiplier must be between {MinSurgeMultiplier} and {MaxSurgeMultiplier}.");
        }

        if (freeDeliveryThreshold.HasValue &&
            !string.Equals(baseFee.Currency, freeDeliveryThreshold.Value.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Free delivery threshold currency must match base fee currency.");
        }

        BaseFee = baseFee;
        SurgeMultiplier = surgeMultiplier;
        FreeDeliveryThreshold = freeDeliveryThreshold;
    }

    public Money BaseFee { get; }

    public decimal SurgeMultiplier { get; }

    public Money? FreeDeliveryThreshold { get; }

    public Money CalculateFee(Money preDiscountItemsSubtotal)
    {
        // Free delivery applies against pre-discount subtotal so applying a coupon never unexpectedly adds a delivery fee.
        // During high-demand surge (e.g., bad weather > 1.0x), free delivery is suspended to balance courier supply.
        if (SurgeMultiplier == MinSurgeMultiplier &&
            FreeDeliveryThreshold.HasValue &&
            preDiscountItemsSubtotal >= FreeDeliveryThreshold.Value)
        {
            return Money.Zero(BaseFee.Currency);
        }

        // Commercial rounding (half away from zero) is what couriers and customers expect to see on a receipt.
        decimal surgedFee = decimal.Round(BaseFee.Amount * SurgeMultiplier, 2, MidpointRounding.AwayFromZero);
        return new Money(surgedFee, BaseFee.Currency);
    }
}
