namespace EmberDelivery.Billing.IntegrationEvents;

public static class PaymentContracts
{
    public sealed record HoldFundsCommand(
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency);

    public sealed record FundsHeldIntegrationEvent(
        Guid OrderId,
        Guid PaymentHoldId);

    public sealed record FundsHoldFailedIntegrationEvent(
        Guid OrderId,
        string Reason);

    public sealed record ReleaseFundsHoldCommand(
        Guid OrderId,
        Guid PaymentHoldId,
        string Reason);

    public sealed record CaptureFundsCommand(
        Guid OrderId,
        Guid PaymentHoldId);
}
