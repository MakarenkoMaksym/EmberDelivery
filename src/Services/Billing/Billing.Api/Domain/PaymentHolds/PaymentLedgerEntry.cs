using EmberDelivery.BuildingBlocks.Domain.Abstractions;

namespace EmberDelivery.Billing.Api.Domain.PaymentHolds;

// Append-only: entries are never mutated or removed, so the ledger stays a faithful audit trail of gateway operations.
public sealed class PaymentLedgerEntry : Entity
{
    private PaymentLedgerEntry()
    {
    }

    internal PaymentLedgerEntry(
        Guid paymentHoldId,
        LedgerEntryType type,
        Money amount,
        string idempotencyKey,
        string gatewayTransactionId,
        DateTimeOffset occurredAtUtc)
    {
        Id = Guid.NewGuid();
        PaymentHoldId = paymentHoldId;
        Type = type;
        Amount = amount;
        IdempotencyKey = idempotencyKey;
        GatewayTransactionId = gatewayTransactionId;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid PaymentHoldId { get; private set; }

    public LedgerEntryType Type { get; private set; }

    public Money Amount { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;

    public string GatewayTransactionId { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; private set; }
}
