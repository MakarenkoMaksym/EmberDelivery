namespace EmberDelivery.Billing.Api.Domain.PaymentHolds;

public enum PaymentHoldStatus
{
    Held = 0,
    Captured = 1,
    Released = 2,
    Failed = 3
}
