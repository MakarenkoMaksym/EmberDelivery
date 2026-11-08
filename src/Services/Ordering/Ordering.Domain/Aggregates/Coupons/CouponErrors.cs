using EmberDelivery.BuildingBlocks.Domain.Results;

namespace EmberDelivery.Ordering.Domain.Aggregates.Coupons;

public static class CouponErrors
{
    public static readonly Error InvalidCode = Error.Validation(
        "Coupon.InvalidCode",
        "Coupon code cannot be empty.");

    public static readonly Error InvalidDiscountValue = Error.Validation(
        "Coupon.InvalidDiscountValue",
        "Discount value must be greater than zero, and percentage discount cannot exceed 100%.");

    public static readonly Error InvalidUsageLimits = Error.Validation(
        "Coupon.InvalidUsageLimits",
        "Usage limits must be at least 1 when specified.");

    public static Error Inactive(string code) => Error.Conflict(
        "Coupon.Inactive",
        $"Coupon '{code}' is no longer active.");

    public static Error Expired(string code) => Error.Conflict(
        "Coupon.Expired",
        $"Coupon '{code}' has expired.");

    public static Error NotValidForRestaurant(string code) => Error.Conflict(
        "Coupon.NotValidForRestaurant",
        $"Coupon '{code}' cannot be applied to orders from this restaurant.");

    public static Error MinimumBasketNotMet(string code, decimal requiredAmount, decimal actualAmount) => Error.Conflict(
        "Coupon.MinimumBasketNotMet",
        $"Coupon '{code}' requires a minimum basket subtotal of {requiredAmount:0.00}, but current subtotal is {actualAmount:0.00}.");

    public static Error TotalUsageLimitReached(string code) => Error.Conflict(
        "Coupon.TotalUsageLimitReached",
        $"Coupon '{code}' has reached its maximum global usage limit.");

    public static Error CustomerUsageLimitReached(string code) => Error.Conflict(
        "Coupon.CustomerUsageLimitReached",
        $"You have already used coupon '{code}' the maximum allowed number of times.");
}
