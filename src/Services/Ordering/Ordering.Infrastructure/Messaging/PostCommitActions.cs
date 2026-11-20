namespace EmberDelivery.Ordering.Infrastructure.Messaging;

// Consumers run inside the EF outbox transaction, so side effects on external stores (e.g. Redis) registered here are
// deferred until PostCommitActionsFilter observes that the transaction has committed.
public sealed class PostCommitActions
{
    private readonly List<Func<CancellationToken, Task>> _actions = [];

    public void Enqueue(Func<CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _actions.Add(action);
    }

    internal IReadOnlyList<Func<CancellationToken, Task>> Drain()
    {
        Func<CancellationToken, Task>[] pending = [.. _actions];
        _actions.Clear();
        return pending;
    }
}
