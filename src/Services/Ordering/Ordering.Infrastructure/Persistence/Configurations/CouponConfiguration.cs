using EmberDelivery.BuildingBlocks.Infrastructure.Persistence.Configurations;
using EmberDelivery.Ordering.Domain.Aggregates.Coupons;
using EmberDelivery.Ordering.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmberDelivery.Ordering.Infrastructure.Persistence.Configurations;

internal sealed class CouponConfiguration : AggregateRootConfiguration<Coupon>
{
    protected override void ConfigureAggregate(EntityTypeBuilder<Coupon> builder)
    {
        builder.ToTable("coupons");

        builder.Property(x => x.Code)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Value)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.ComplexProperty(x => x.MinimumBasketSubtotal, money =>
        {
            money.Property(m => m.Amount).HasColumnName("min_basket_amount").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("min_basket_currency").HasMaxLength(3);
        });

        builder.Property(x => x.MaxDiscountCap)
            .HasConversion(
                v => v.HasValue ? v.Value.Amount : (decimal?)null,
                v => v.HasValue ? new Money(v.Value, "UAH") : null)
            .HasColumnName("max_discount_cap_amount")
            .HasPrecision(18, 2);

        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        // Only one active coupon can hold a given promo code at a time.
        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasFilter("is_active = true");
    }
}
