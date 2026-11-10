namespace EmberDelivery.Billing.Api.Infrastructure.PaymentGateway;

public sealed class FakePaymentGatewayOptions
{
    public const string SectionName = "PaymentGateway:Fake";

    // Lets the decline path be exercised end-to-end by simply placing an expensive enough order.
    public decimal DeclineAmountAbove { get; set; } = 10_000m;
}
