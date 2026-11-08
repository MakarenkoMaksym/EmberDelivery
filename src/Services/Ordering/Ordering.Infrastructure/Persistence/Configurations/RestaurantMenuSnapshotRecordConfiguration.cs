using EmberDelivery.Ordering.Infrastructure.Persistence.Projections;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmberDelivery.Ordering.Infrastructure.Persistence.Configurations;

internal sealed class RestaurantMenuSnapshotRecordConfiguration : IEntityTypeConfiguration<RestaurantMenuSnapshotRecord>
{
    public void Configure(EntityTypeBuilder<RestaurantMenuSnapshotRecord> builder)
    {
        builder.ToTable("restaurant_menu_snapshots");

        builder.HasKey(x => x.RestaurantId);

        builder.Property(x => x.RestaurantId)
            .ValueGeneratedNever();

        // Store the menu snapshot as native PostgreSQL jsonb for single-read deserialization without joins.
        builder.Property(x => x.PayloadJson)
            .HasColumnName("payload_json")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();
    }
}
