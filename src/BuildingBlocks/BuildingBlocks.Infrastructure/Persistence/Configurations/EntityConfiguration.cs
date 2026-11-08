using EmberDelivery.BuildingBlocks.Domain.Abstractions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmberDelivery.BuildingBlocks.Infrastructure.Persistence.Configurations;

public abstract class EntityConfiguration<TEntity, TId> : IEntityTypeConfiguration<TEntity>
    where TEntity : Entity<TId>
    where TId : notnull
{
    public void Configure(EntityTypeBuilder<TEntity> builder)
    {
        builder.HasKey(x => x.Id);

        // Domain entities generate their own identifiers in memory; prevent database-side identity generation.
        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        ConfigureEntity(builder);
    }

    protected abstract void ConfigureEntity(EntityTypeBuilder<TEntity> builder);
}

public abstract class EntityConfiguration<TEntity> : EntityConfiguration<TEntity, Guid>
    where TEntity : Entity<Guid>
{
}
