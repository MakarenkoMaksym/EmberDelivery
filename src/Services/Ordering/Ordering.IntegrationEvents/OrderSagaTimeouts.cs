namespace EmberDelivery.Ordering.IntegrationEvents;

public static class OrderSagaTimeouts
{
    public sealed record TriggerCourierDispatchTimeout(
    Guid OrderId);
}
