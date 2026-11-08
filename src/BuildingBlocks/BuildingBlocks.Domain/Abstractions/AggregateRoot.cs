namespace EmberDelivery.BuildingBlocks.Domain.Abstractions;

public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = [];
    private bool _isVersionIncremented;

    public uint Version { get; protected set; } = 1;

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }

    protected void IncrementVersion()
    {
        // Guard against incrementing the version multiple times within the same unit of work transaction.
        if (_isVersionIncremented)
        {
            return;
        }

        Version++;
        _isVersionIncremented = true;
    }

    protected void Raise(IDomainEvent domainEvent)
    {
        // Automatically bump the aggregate root concurrency token whenever a state change raises a domain event,
        // ensuring EF Core marks the root as Modified even if only child entities were mutated.
        IncrementVersion();
        _domainEvents.Add(domainEvent);
    }
}

// Syntactic sugar for the default case where the aggregate identifier is a Guid.
public abstract class AggregateRoot : AggregateRoot<Guid>
{
}
