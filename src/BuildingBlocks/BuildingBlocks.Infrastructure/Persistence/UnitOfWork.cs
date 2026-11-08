using EmberDelivery.BuildingBlocks.Application.Abstractions;
using EmberDelivery.BuildingBlocks.Domain.Abstractions;

using MediatR;

using Microsoft.EntityFrameworkCore;

namespace EmberDelivery.BuildingBlocks.Infrastructure.Persistence;

public sealed class UnitOfWork<TDbContext>(TDbContext dbContext, IPublisher publisher) : IUnitOfWork
    where TDbContext : DbContext
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var domainEntities = dbContext.ChangeTracker
                .Entries<IAggregateRoot>()
                .Where(x => x.Entity.DomainEvents.Count > 0)
                .Select(x => x.Entity)
                .ToList();

            if (domainEntities.Count == 0)
            {
                break;
            }

            var domainEvents = domainEntities
                .SelectMany(x => x.DomainEvents)
                .ToList();

            foreach (IAggregateRoot entity in domainEntities)
            {
                entity.ClearDomainEvents();
            }

            foreach (IDomainEvent domainEvent in domainEvents)
            {
                await publisher.Publish(domainEvent, cancellationToken);
            }
        }

        return await dbContext.SaveChangesAsync(cancellationToken);
    }
}
