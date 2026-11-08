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

    public Money? MaxDiscountCap { get; private set; }

    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    public Guid? RestrictedToRestaurantId { get; private set; }

    public int? MaxTotalRedemptions { get; private set; }

    public int MaxRedemptionsPerCustomer { get; private set; }

    public int TotalRedemptionsCount { get; private set; }

    public bool IsActive { get; private set; }

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

        if (value <= 0m || (type == DiscountType.Percentage && value > 100m))
        {
            return Result.Failure<Coupon>(CouponErrors.InvalidDiscountValue);
        }

        if (maxRedemptionsPerCustomer <= 0 || (maxTotalRedemptions.HasValue && maxTotalRedemptions.Value <= 0))
        {
            return Result.Failure<Coupon>(CouponErrors.InvalidUsageLimits);
        }

        Coupon coupon = new()
        {
            Id = Guid.NewGuid(),
            Code = code.Trim().ToUpperInvariant(),
            Type = type,
            Value = value,
            MinimumBasketSubtotal = minimumBasketSubtotal,
            MaxDiscountCap = maxDiscountCap,
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
            DiscountType.Percentage => new Money(itemsSubtotal.Amount * (Value / 100m), itemsSubtotal.Currency),
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
}
