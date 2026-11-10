using System.Collections.Concurrent;

using Microsoft.Extensions.Options;

namespace EmberDelivery.Billing.Api.Infrastructure.PaymentGateway;

internal sealed class FakePaymentGateway(IOptions<FakePaymentGatewayOptions> options) : IPaymentGateway
{
    // Emulates provider-side idempotency: a real PSP replays its original response for a repeated key, which is what
    // makes calling it before our own commit safe. This cache is process-local and therefore only a stand-in.
    private readonly ConcurrentDictionary<string, PaymentGatewayResult> _responsesByIdempotencyKey =
        new(StringComparer.Ordinal);

    public Task<PaymentGatewayResult> AuthorizeAsync(
        AuthorizePaymentRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        PaymentGatewayResult result = _responsesByIdempotencyKey.GetOrAdd(
            request.IdempotencyKey,
            _ => request.Amount.Amount > options.Value.DeclineAmountAbove
                ? PaymentGatewayResult.Declined("Insufficient funds.")
                : PaymentGatewayResult.Approved(NewTransactionId("auth")));

        return Task.FromResult(result);
    }

    public Task<PaymentGatewayResult> CaptureAsync(
        CapturePaymentRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        PaymentGatewayResult result = _responsesByIdempotencyKey.GetOrAdd(
            request.IdempotencyKey,
            _ => PaymentGatewayResult.Approved(NewTransactionId("capt")));

        return Task.FromResult(result);
    }

    public Task<PaymentGatewayResult> ReleaseAsync(
        ReleasePaymentRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        PaymentGatewayResult result = _responsesByIdempotencyKey.GetOrAdd(
            request.IdempotencyKey,
            _ => PaymentGatewayResult.Approved(NewTransactionId("rel")));

        return Task.FromResult(result);
    }

    private static string NewTransactionId(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
