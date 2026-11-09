using EmberDelivery.BuildingBlocks.Domain.Abstractions;
using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.Ordering.Domain.ValueObjects;

namespace EmberDelivery.Ordering.Domain.Aggregates.Coupons;

public sealed class Coupon : AggregateRoot
{
    private Coupon()
    {
    }

    public string Code { get; private set; } = string.Empty;

    public DiscountType Type { get; private set; }

    public decimal Value { get; private set; }

    public Money MinimumBasketSubtotal { get; private set; }

    // A coupon is single-currency, so only the cap amount is persisted; the currency is always borrowed from
    // MinimumBasketSubtotal and therefore cannot drift from it after a round trip through the database.
    public Money? MaxDiscountCap => MaxDiscountCapAmount.HasValue
        ? new Money(MaxDiscountCapAmount.Value, MinimumBasketSubtotal.Currency)
        : null;

    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    public Guid? RestrictedToRestaurantId { get; private set; }

    public int? MaxTotalRedemptions { get; private set; }

    public int MaxRedemptionsPerCustomer { get; private set; }

    public int TotalRedemptionsCount { get; private set; }

    public bool IsActive { get; private set; }

    private decimal? MaxDiscountCapAmount { get; set; }

    public static Result<Coupon> Create(
        string code,
        DiscountType type,
        decimal value,
        Money minimumBasketSubtotal,
        int maxRedemptionsPerCustomer = 1,
        int? maxTotalRedemptions = null,
        Money? maxDiscountCap = null,
        DateTimeOffset? expiresAtUtc = null,
        Guid? restrictedToRestaurantId = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure<Coupon>(CouponErrors.InvalidCode);
        }

        // Values with sub-cent precision would be silently truncated by storage and break Money construction later.
        if (value <= 0m ||
            decimal.Round(value, 2) != value ||
            (type == DiscountType.Percentage && value > 100m))
        {
            return Result.Failure<Coupon>(CouponErrors.InvalidDiscountValue);
        }

        if (maxRedemptionsPerCustomer <= 0 || (maxTotalRedemptions.HasValue && maxTotalRedemptions.Value <= 0))
        {
            return Result.Failure<Coupon>(CouponErrors.InvalidUsageLimits);
        }

        if (maxDiscountCap.HasValue &&
            !string.Equals(maxDiscountCap.Value.Currency, minimumBasketSubtotal.Currency, StringComparison.Ordinal))
        {
            return Result.Failure<Coupon>(CouponErrors.CurrencyMismatch);
        }

        Coupon coupon = new()
        {
            Id = Guid.NewGuid(),
            Code = code.Trim().ToUpperInvariant(),
            Type = type,
            Value = value,
            MinimumBasketSubtotal = minimumBasketSubtotal,
            MaxDiscountCapAmount = maxDiscountCap?.Amount,
            ExpiresAtUtc = expiresAtUtc,
            RestrictedToRestaurantId = restrictedToRestaurantId,
            MaxTotalRedemptions = maxTotalRedemptions,
            MaxRedemptionsPerCustomer = maxRedemptionsPerCustomer,
            TotalRedemptionsCount = 0,
            IsActive = true
        };

        return Result.Success(coupon);
    }

    public Result<Money> EvaluateDiscount(
        Guid restaurantId,
        Money itemsSubtotal,
        int customerPreviousRedemptions,
        DateTimeOffset currentUtcTime)
    {
        if (!IsActive)
        {
            return Result.Failure<Money>(CouponErrors.Inactive(Code));
        }

        if (ExpiresAtUtc.HasValue && currentUtcTime > ExpiresAtUtc.Value)
        {
            return Result.Failure<Money>(CouponErrors.Expired(Code));
        }

        if (MaxTotalRedemptions.HasValue && TotalRedemptionsCount >= MaxTotalRedemptions.Value)
        {
            return Result.Failure<Money>(CouponErrors.TotalUsageLimitReached(Code));
        }

        if (customerPreviousRedemptions >= MaxRedemptionsPerCustomer)
        {
            return Result.Failure<Money>(CouponErrors.CustomerUsageLimitReached(Code));
        }

        if (RestrictedToRestaurantId.HasValue && RestrictedToRestaurantId.Value != restaurantId)
        {
            return Result.Failure<Money>(CouponErrors.NotValidForRestaurant(Code));
        }

        if (itemsSubtotal.Amount < MinimumBasketSubtotal.Amount)
        {
            return Result.Failure<Money>(
                CouponErrors.MinimumBasketNotMet(Code, MinimumBasketSubtotal.Amount, itemsSubtotal.Amount));
        }

        Money rawDiscount = Type switch
        {
            DiscountType.FixedAmount => new Money(Value, itemsSubtotal.Currency),
            DiscountType.Percentage => new Money(CalculatePercentageDiscount(itemsSubtotal.Amount), itemsSubtotal.Currency),
            _ => throw new InvalidOperationException($"Unsupported discount type: {Type}")
        };

        if (MaxDiscountCap.HasValue && rawDiscount.Amount > MaxDiscountCap.Value.Amount)
        {
            rawDiscount = MaxDiscountCap.Value;
        }

        if (rawDiscount.Amount > itemsSubtotal.Amount)
        {
            rawDiscount = itemsSubtotal;
        }

        return Result.Success(rawDiscount);
    }

    public Result<CouponRedemption> Redeem(
        Guid customerId,
        Guid orderId,
        Guid restaurantId,
        Money itemsSubtotal,
        int customerPreviousRedemptions,
        TimeProvider timeProvider)
    {
        DateTimeOffset nowUtc = timeProvider.GetUtcNow();

        Result<Money> discountResult = EvaluateDiscount(
            restaurantId,
            itemsSubtotal,
            customerPreviousRedemptions,
            nowUtc);

        if (discountResult.IsFailure)
        {
            return Result.Failure<CouponRedemption>(discountResult.Error);
        }

        TotalRedemptionsCount++;
        IncrementVersion();

        CouponRedemption redemption = new(
            id: Guid.NewGuid(),
            couponId: Id,
            customerId: customerId,
            orderId: orderId,
            discountApplied: discountResult.Value,
            redeemedAtUtc: nowUtc);

        return Result.Success(redemption);
    }

    public void ReleaseRedemption(CouponRedemption redemption)
    {
        if (redemption.CouponId != Id)
        {
            throw new InvalidOperationException("Cannot release a redemption that belongs to a different coupon.");
        }

        if (TotalRedemptionsCount <= 0)
        {
            return;
        }

        TotalRedemptionsCount--;
        IncrementVersion();
    }

    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        IncrementVersion();
    }

    // Truncating toward zero guarantees the discount never exceeds the advertised percentage by a fractional cent.
    private decimal CalculatePercentageDiscount(decimal itemsSubtotalAmount) =>
        decimal.Round(itemsSubtotalAmount * Value / 100m, 2, MidpointRounding.ToZero);
}
