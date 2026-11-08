using EmberDelivery.Ordering.Domain.Aggregates.Coupons;

using Microsoft.EntityFrameworkCore;

namespace EmberDelivery.Ordering.Infrastructure.Persistence.Repositories;

internal sealed class CouponRepository(OrderingDbContext dbContext) : ICouponRepository
{
    public Task<Coupon?> GetActiveByCodeAsync(string normalizedCode, CancellationToken cancellationToken = default)
    {
        return dbContext.Coupons
            .FirstOrDefaultAsync(x => x.Code == normalizedCode && x.IsActive, cancellationToken);
    }

    public Task<Coupon?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return dbContext.Coupons
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public Task<int> CountCustomerRedemptionsAsync(
        Guid couponId,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.CouponRedemptions
            .CountAsync(x => x.CouponId == couponId && x.CustomerId == customerId, cancellationToken);
    }

    public Task<CouponRedemption?> GetRedemptionByOrderIdAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.CouponRedemptions
            .FirstOrDefaultAsync(x => x.OrderId == orderId, cancellationToken);
    }

    public void Add(Coupon coupon)
    {
        dbContext.Coupons.Add(coupon);
    }

    public void AddRedemption(CouponRedemption redemption)
    {
        dbContext.CouponRedemptions.Add(redemption);
    }

    public void RemoveRedemption(CouponRedemption redemption)
    {
        dbContext.CouponRedemptions.Remove(redemption);
    }
}
