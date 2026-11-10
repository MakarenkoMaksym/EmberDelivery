using EmberDelivery.Billing.Api.Domain.PaymentHolds;
using EmberDelivery.BuildingBlocks.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmberDelivery.Billing.Api.Infrastructure.Persistence.Configurations;

internal sealed class PaymentHoldConfiguration : AggregateRootConfiguration<PaymentHold>
{
    protected override void ConfigureAggregate(EntityTypeBuilder<PaymentHold> builder)
    {
        builder.ToTable("payment_holds");

        builder.Property(x => x.OrderId)
            .IsRequired();

        builder.Property(x => x.CustomerId)
            .IsRequired();

        builder.ComplexProperty(x => x.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("amount").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("currency").HasMaxLength(3);
        });

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.GatewayAuthorizationId)
            .HasMaxLength(128);

        builder.Property(x => x.FailureReason)
            .HasMaxLength(PaymentHold.MaxReasonLength);

        builder.Property(x => x.ReleaseReason)
            .HasMaxLength(PaymentHold.MaxReasonLength);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        // Ledger rows are financial records; deleting a hold must never silently erase its money movements.
        builder.HasMany(x => x.LedgerEntries)
            .WithOne()
            .HasForeignKey(x => x.PaymentHoldId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(x => x.LedgerEntries)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // One hold per order: with no MessageId-based inbox, this is what turns two concurrent HoldFunds deliveries
        // that both passed the existence check into exactly one committed hold and one outcome event.
        builder.HasIndex(x => x.OrderId)
            .IsUnique();

        builder.HasIndex(x => x.CustomerId);
    }
}
