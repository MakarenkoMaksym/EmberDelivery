using EmberDelivery.Billing.Api.Domain.PaymentHolds;

using MassTransit;

using Microsoft.EntityFrameworkCore;

namespace EmberDelivery.Billing.Api.Infrastructure.Persistence;

public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public DbSet<PaymentHold> PaymentHolds => Set<PaymentHold>();

    public DbSet<PaymentLedgerEntry> PaymentLedgerEntries => Set<PaymentLedgerEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BillingDbContext).Assembly);

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
