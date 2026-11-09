using EmberDelivery.BuildingBlocks.Infrastructure.Persistence.Configurations;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmberDelivery.Ordering.Infrastructure.Persistence.Configurations;

internal sealed class OrderLineConfiguration : EntityConfiguration<OrderLine>
{
    protected override void ConfigureEntity(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("order_lines");

        builder.Property(x => x.MenuItemId)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Quantity)
            .IsRequired();

        builder.ComplexProperty(x => x.BaseUnitPrice, money =>
        {
            money.Property(m => m.Amount).HasColumnName("base_unit_price_amount").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("base_unit_price_currency").HasMaxLength(3);
        });

        builder.ComplexProperty(x => x.LineTotal, money =>
        {
            money.Property(m => m.Amount).HasColumnName("line_total_amount").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("line_total_currency").HasMaxLength(3);
        });

        builder.OwnsMany(x => x.Modifiers, modBuilder =>
        {
            modBuilder.ToTable("order_line_modifiers");

            modBuilder.WithOwner().HasForeignKey("OrderLineId");

            modBuilder.Property<int>("Id");
            modBuilder.HasKey("Id");

            modBuilder.Property(m => m.ModifierId).IsRequired();
            modBuilder.Property(m => m.Name).HasMaxLength(150).IsRequired();

            // EF Core 10 has no ComplexProperty on owned types, so Money is flattened into the two columns it needs.
            modBuilder.Ignore(m => m.Price);
            modBuilder.Property<decimal>("PriceAmount")
                .HasColumnName("price_amount")
                .HasPrecision(18, 2)
                .IsRequired();
            modBuilder.Property<string>("PriceCurrency")
                .HasColumnName("price_currency")
                .HasMaxLength(3)
                .IsRequired();
        });

        builder.Navigation(x => x.Modifiers)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
