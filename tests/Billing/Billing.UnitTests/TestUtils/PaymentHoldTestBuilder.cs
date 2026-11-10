using EmberDelivery.Billing.Api.Domain;
using EmberDelivery.Billing.Api.Domain.PaymentHolds;

namespace EmberDelivery.Billing.UnitTests.TestUtils;

internal static class PaymentHoldTestBuilder
{
    public const decimal DefaultAmount = 450.50m;
    public const string DefaultCurrency = "UAH";
    public const string AuthorizationId = "auth_test";
    public const string CaptureTransactionId = "capt_test";
    public const string ReleaseTransactionId = "rel_test";

    public static readonly Guid OrderId = Guid.Parse("4b1d1a52-1f0e-4f43-9a3a-0d5a3c0f6a11");
    public static readonly Guid CustomerId = Guid.Parse("8c2f7e41-6d9b-4a7e-b3c2-5e1f0a9d7b22");
    public static readonly DateTimeOffset PlacedAtUtc = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    public static Money DefaultMoney => Money.Create(DefaultAmount, DefaultCurrency).Value;

    public static PaymentHold Held() =>
        PaymentHold.Place(OrderId, CustomerId, DefaultMoney, AuthorizationId, PlacedAtUtc).Value;

    public static PaymentHold Captured()
    {
        PaymentHold hold = Held();
        hold.Capture(CaptureTransactionId, PlacedAtUtc.AddMinutes(40));
        return hold;
    }

    public static PaymentHold Released()
    {
        PaymentHold hold = Held();
        hold.Release("Kitchen rejected the order.", ReleaseTransactionId, PlacedAtUtc.AddMinutes(5));
        return hold;
    }

    public static PaymentHold Failed() =>
        PaymentHold.Decline(OrderId, CustomerId, DefaultMoney, "Insufficient funds.", PlacedAtUtc).Value;
}
