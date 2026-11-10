namespace EmberDelivery.Billing.Api.Infrastructure.PaymentGateway;

public sealed record PaymentGatewayResult
{
    private PaymentGatewayResult(bool isApproved, string? transactionId, string? declineReason)
    {
        IsApproved = isApproved;
        TransactionId = transactionId;
        DeclineReason = declineReason;
    }

    public bool IsApproved { get; }

    public string? TransactionId { get; }

    public string? DeclineReason { get; }

    public static PaymentGatewayResult Approved(string transactionId) => new(true, transactionId, null);

    public static PaymentGatewayResult Declined(string reason) => new(false, null, reason);
}
