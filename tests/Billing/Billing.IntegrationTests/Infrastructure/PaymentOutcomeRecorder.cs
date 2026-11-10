using System.Collections.Concurrent;

using static EmberDelivery.Billing.IntegrationEvents.PaymentContracts;

namespace EmberDelivery.Billing.IntegrationTests.Infrastructure;

// Stands in for the Ordering saga: subscribes to Billing's outcome events on the real broker, so a test only sees an
// event after it went through the bus outbox, was committed, and was actually delivered by the outbox delivery service.
public sealed class PaymentOutcomeRecorder
{
    private readonly ConcurrentQueue<object> _outcomes = new();

    public void Record(object outcome) => _outcomes.Enqueue(outcome);

    public void Reset() => _outcomes.Clear();

    public IReadOnlyList<FundsHeldIntegrationEvent> FundsHeldFor(Guid orderId) =>
        _outcomes.OfType<FundsHeldIntegrationEvent>().Where(e => e.OrderId == orderId).ToList();

    public IReadOnlyList<FundsHoldFailedIntegrationEvent> FundsHoldFailedFor(Guid orderId) =>
        _outcomes.OfType<FundsHoldFailedIntegrationEvent>().Where(e => e.OrderId == orderId).ToList();

    public int OutcomeCountFor(Guid orderId) => FundsHeldFor(orderId).Count + FundsHoldFailedFor(orderId).Count;
}
