namespace EmberDelivery.Ordering.IntegrationEvents;

public static class OrderSagaTimeouts
{
    public sealed record TriggerCourierDispatchTimeout(
        Guid OrderId);

    public sealed record PaymentHoldTimeoutExpired(
        Guid OrderId);

    public sealed record KitchenConfirmationTimeoutExpired(
        Guid OrderId);

    public sealed record CourierPickupTimeoutExpired(
        Guid OrderId);
}
