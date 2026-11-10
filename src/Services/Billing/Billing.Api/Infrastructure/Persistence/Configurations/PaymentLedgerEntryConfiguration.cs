using EmberDelivery.Billing.Api.Domain.PaymentHolds;
using EmberDelivery.BuildingBlocks.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmberDelivery.Billing.Api.Infrastructure.Persistence.Configurations;

internal sealed class PaymentLedgerEntryConfiguration : EntityConfiguration<PaymentLedgerEntry>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PaymentLedgerEntry> builder)
    {
        builder.ToTable("payment_ledger_entries");

        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.ComplexProperty(x => x.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("amount").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("currency").HasMaxLength(3);
        });

        builder.Property(x => x.IdempotencyKey)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.GatewayTransactionId)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.OccurredAtUtc)
            .IsRequired();

        // The database enforces what the aggregate promises: each gateway operation is recorded at most once,
        // even if two concurrent consumers both pass the in-memory guard.
        builder.HasIndex(x => x.IdempotencyKey)
            .IsUnique();
    }
}
