namespace EmberDelivery.Billing.Api.Infrastructure.PaymentGateway;

// Business declines come back as PaymentGatewayResult so callers can record them; transport or provider outages
// surface as PaymentGatewayUnavailableException so the message pipeline retries with the same idempotency key.
public interface IPaymentGateway
{
    Task<PaymentGatewayResult> AuthorizeAsync(AuthorizePaymentRequest request, CancellationToken cancellationToken);

    Task<PaymentGatewayResult> CaptureAsync(CapturePaymentRequest request, CancellationToken cancellationToken);

    Task<PaymentGatewayResult> ReleaseAsync(ReleasePaymentRequest request, CancellationToken cancellationToken);
}
