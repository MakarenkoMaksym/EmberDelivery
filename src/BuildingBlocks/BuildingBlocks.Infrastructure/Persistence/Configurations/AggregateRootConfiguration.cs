using EmberDelivery.BuildingBlocks.Domain.Abstractions;

using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmberDelivery.BuildingBlocks.Infrastructure.Persistence.Configurations;

public abstract class AggregateRootConfiguration<TAggregate, TId> : EntityConfiguration<TAggregate, TId>
    where TAggregate : AggregateRoot<TId>
    where TId : notnull
{
    protected sealed override void ConfigureEntity(EntityTypeBuilder<TAggregate> builder)
    {
        // Enforce optimistic concurrency check (WHERE version = @originalVersion) on every aggregate update.
        builder.Property(x => x.Version)
            .IsConcurrencyToken();

        // Domain events are transient in-memory notifications dispatched by UnitOfWork before commit.
        builder.Ignore(x => x.DomainEvents);

        ConfigureAggregate(builder);
    }

    protected abstract void ConfigureAggregate(EntityTypeBuilder<TAggregate> builder);
}

public abstract class AggregateRootConfiguration<TAggregate> : AggregateRootConfiguration<TAggregate, Guid>
    where TAggregate : AggregateRoot<Guid>
{
}
