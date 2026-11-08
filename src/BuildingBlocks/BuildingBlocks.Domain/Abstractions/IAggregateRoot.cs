namespace EmberDelivery.BuildingBlocks.Domain.Abstractions;

public interface IAggregateRoot
{
    uint Version { get; }

    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}
