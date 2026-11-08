using EmberDelivery.Ordering.Application.Sagas.OrderFulfillment;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmberDelivery.Ordering.Infrastructure.Persistence.Configurations;

internal sealed class OrderSagaStateConfiguration : IEntityTypeConfiguration<OrderSagaState>
{
    public void Configure(EntityTypeBuilder<OrderSagaState> builder)
    {
        builder.ToTable("order_saga_states");

        builder.HasKey(x => x.CorrelationId);

        builder.Property(x => x.CorrelationId)
            .ValueGeneratedNever();

        builder.Property(x => x.Version)
            .IsConcurrencyToken();

        builder.Property(x => x.CurrentState)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.TotalAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.Currency)
            .HasMaxLength(3);

        builder.Property(x => x.FailureReason)
            .HasMaxLength(500);
    }
}
