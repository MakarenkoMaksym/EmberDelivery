using System.Collections.Concurrent;

using EmberDelivery.Billing.Api.Features;

using MassTransit;

namespace EmberDelivery.Billing.IntegrationTests.Infrastructure;

public sealed record ConsumptionOutcome(Guid MessageId, Exception? Fault)
{
    public bool Succeeded => Fault is null;
}

// Billing has no inbox table to poll anymore, so tests learn that a message was fully processed (including its
// SaveChanges) from the bus itself. Only terminal outcomes are recorded: a retryable fault may still be followed
// by a successful attempt, whereas PaymentHoldOperationException is never retried and goes to the error queue.
public sealed class ConsumptionTracker : IConsumeObserver
{
    private readonly ConcurrentDictionary<Guid, ConsumptionOutcome> _outcomes = new();

    public void Reset() => _outcomes.Clear();

    public bool TryGetOutcome(Guid messageId, out ConsumptionOutcome? outcome) =>
        _outcomes.TryGetValue(messageId, out outcome);

    public Task PreConsume<T>(ConsumeContext<T> context)
        where T : class =>
        Task.CompletedTask;

    public Task PostConsume<T>(ConsumeContext<T> context)
        where T : class
    {
        Record(context.MessageId, fault: null);
        return Task.CompletedTask;
    }

    public Task ConsumeFault<T>(ConsumeContext<T> context, Exception exception)
        where T : class
    {
        if (exception is PaymentHoldOperationException)
        {
            Record(context.MessageId, exception);
        }

        return Task.CompletedTask;
    }

    private void Record(Guid? messageId, Exception? fault)
    {
        if (messageId is { } id)
        {
            _outcomes.TryAdd(id, new ConsumptionOutcome(id, fault));
        }
    }
}
