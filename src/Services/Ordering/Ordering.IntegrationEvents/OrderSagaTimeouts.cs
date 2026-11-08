namespace EmberDelivery.Ordering.IntegrationEvents;

public class OrderSagaTimeouts
{
    public sealed record TriggerCourierDispatchTimeout(
    Guid OrderId);
}
