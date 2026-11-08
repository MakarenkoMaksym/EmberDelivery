using EmberDelivery.BuildingBlocks.Infrastructure.Persistence.Configurations;
using EmberDelivery.Ordering.Domain.Aggregates.Coupons;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmberDelivery.Ordering.Infrastructure.Persistence.Configurations;

internal sealed class CouponRedemptionConfiguration : EntityConfiguration<CouponRedemption>
{
    protected override void ConfigureEntity(EntityTypeBuilder<CouponRedemption> builder)
    {
        builder.ToTable("coupon_redemptions");

        builder.ComplexProperty(x => x.DiscountApplied, money =>
        {
            money.Property(m => m.Amount).HasColumnName("discount_applied_amount").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("discount_applied_currency").HasMaxLength(3);
        });

        builder.HasIndex(x => new { x.CouponId, x.CustomerId });

        builder.HasIndex(x => x.OrderId)
            .IsUnique();
    }
}
