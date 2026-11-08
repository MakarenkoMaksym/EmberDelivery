using EmberDelivery.BuildingBlocks.Infrastructure.Persistence.Configurations;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmberDelivery.Ordering.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : AggregateRootConfiguration<Order>
{
    protected override void ConfigureAggregate(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.Property(x => x.CustomerId)
            .IsRequired();

        builder.Property(x => x.RestaurantId)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.AppliedCouponCode)
            .HasMaxLength(64);

        builder.Property(x => x.CancellationReason)
            .HasMaxLength(500);

        builder.OwnsOne(x => x.DeliveryAddress, address =>
        {
            address.Property(a => a.Street).HasColumnName("delivery_street").HasMaxLength(200).IsRequired();
            address.Property(a => a.Building).HasColumnName("delivery_building").HasMaxLength(50).IsRequired();
            address.Property(a => a.Apartment).HasColumnName("delivery_apartment").HasMaxLength(50);
            address.Property(a => a.DeliveryNotes).HasColumnName("delivery_notes").HasMaxLength(500);
            address.Property(a => a.Latitude).HasColumnName("delivery_latitude").IsRequired();
            address.Property(a => a.Longitude).HasColumnName("delivery_longitude").IsRequired();
        });

        builder.ComplexProperty(x => x.ItemsSubtotal, money =>
        {
            money.Property(m => m.Amount).HasColumnName("items_subtotal_amount").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("items_subtotal_currency").HasMaxLength(3);
        });

        builder.ComplexProperty(x => x.DeliveryFee, money =>
        {
            money.Property(m => m.Amount).HasColumnName("delivery_fee_amount").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("delivery_fee_currency").HasMaxLength(3);
        });

        builder.ComplexProperty(x => x.DiscountAmount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("discount_amount").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("discount_currency").HasMaxLength(3);
        });

        builder.ComplexProperty(x => x.TotalPrice, money =>
        {
            money.Property(m => m.Amount).HasColumnName("total_price_amount").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("total_price_currency").HasMaxLength(3);
        });

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey("OrderId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => x.RestaurantId);
    }
}
