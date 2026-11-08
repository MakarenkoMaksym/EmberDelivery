namespace EmberDelivery.Ordering.Domain.Aggregates.Coupons;

public interface ICouponRepository
{
    Task<Coupon?> GetActiveByCodeAsync(string normalizedCode, CancellationToken cancellationToken = default);

    Task<Coupon?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<int> CountCustomerRedemptionsAsync(Guid couponId, Guid customerId, CancellationToken cancellationToken = default);

    Task<CouponRedemption?> GetRedemptionByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);

    void Add(Coupon coupon);

    void AddRedemption(CouponRedemption redemption);

    void RemoveRedemption(CouponRedemption redemption);
}
