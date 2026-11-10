using EmberDelivery.Billing.Api.Domain;

namespace EmberDelivery.Billing.Api.Infrastructure.PaymentGateway;

public sealed record AuthorizePaymentRequest(
    string IdempotencyKey,
    Guid CustomerId,
    Money Amount);

public sealed record CapturePaymentRequest(
    string IdempotencyKey,
    string AuthorizationId,
    Money Amount);

public sealed record ReleasePaymentRequest(
    string IdempotencyKey,
    string AuthorizationId);
