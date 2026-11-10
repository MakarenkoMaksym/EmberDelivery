using EmberDelivery.BuildingBlocks.Domain.Abstractions;
using EmberDelivery.BuildingBlocks.Domain.Results;

namespace EmberDelivery.Billing.Api.Domain.PaymentHolds;

public sealed class PaymentHold : AggregateRoot
{
    public const int MaxReasonLength = 500;

    private readonly List<PaymentLedgerEntry> _ledgerEntries = [];

    private PaymentHold()
    {
    }

    public Guid OrderId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Money Amount { get; private set; }

    public PaymentHoldStatus Status { get; private set; }

    public string? GatewayAuthorizationId { get; private set; }

    public string? FailureReason { get; private set; }

    public string? ReleaseReason { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IReadOnlyCollection<PaymentLedgerEntry> LedgerEntries => _ledgerEntries.AsReadOnly();

    // Keys derive from the order, a durable business identity, not from message ids or freshly generated hold ids:
    // a retried or re-published command then reaches the gateway with the same key even if the process crashed
    // between the gateway call and the database commit, and the provider replays its original answer.
    public static string IdempotencyKeyFor(Guid orderId, LedgerEntryType operation)
    {
        string operationName = operation switch
        {
            LedgerEntryType.Authorization => "authorize",
            LedgerEntryType.Capture => "capture",
            LedgerEntryType.Release => "release",
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unsupported ledger operation.")
        };

        return $"order-{orderId:N}-{operationName}";
    }

    // Exposed separately from Place so callers can reject a request before any irreversible gateway side effect.
    public static Result ValidateAmount(Money amount) =>
        amount.IsPositive ? Result.Success() : Result.Failure(PaymentHoldErrors.NonPositiveAmount);

    public static Result<PaymentHold> Place(
        Guid orderId,
        Guid customerId,
        Money amount,
        string gatewayAuthorizationId,
        DateTimeOffset nowUtc)
    {
        if (orderId == Guid.Empty || customerId == Guid.Empty)
        {
            return Result.Failure<PaymentHold>(PaymentHoldErrors.MissingIdentifiers);
        }

        Result amountValidation = ValidateAmount(amount);
        if (amountValidation.IsFailure)
        {
            return Result.Failure<PaymentHold>(amountValidation.Error);
        }

        if (string.IsNullOrWhiteSpace(gatewayAuthorizationId))
        {
            return Result.Failure<PaymentHold>(PaymentHoldErrors.MissingGatewayReference);
        }

        PaymentHold hold = new()
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            Status = PaymentHoldStatus.Held,
            GatewayAuthorizationId = gatewayAuthorizationId,
            CreatedAtUtc = nowUtc
        };

        hold.AppendEntry(LedgerEntryType.Authorization, gatewayAuthorizationId, nowUtc);

        return Result.Success(hold);
    }

    // A declined authorization is persisted rather than dropped: the unique order constraint then turns any later
    // duplicate HoldFunds command into a no-op instead of a second authorization attempt on the customer's card.
    public static Result<PaymentHold> Decline(
        Guid orderId,
        Guid customerId,
        Money amount,
        string reason,
        DateTimeOffset nowUtc)
    {
        if (orderId == Guid.Empty || customerId == Guid.Empty)
        {
            return Result.Failure<PaymentHold>(PaymentHoldErrors.MissingIdentifiers);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure<PaymentHold>(PaymentHoldErrors.MissingReason);
        }

        PaymentHold hold = new()
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            Status = PaymentHoldStatus.Failed,
            FailureReason = NormalizeReason(reason),
            CreatedAtUtc = nowUtc
        };

        return Result.Success(hold);
    }

    // Repeating a completed transition is reported as success so that redelivered commands are harmless.
    public Result EnsureCanCapture() => Status switch
    {
        PaymentHoldStatus.Held or PaymentHoldStatus.Captured => Result.Success(),
        PaymentHoldStatus.Released => Result.Failure(PaymentHoldErrors.AlreadyReleased(Id)),
        PaymentHoldStatus.Failed => Result.Failure(PaymentHoldErrors.NotAuthorized(Id)),
        _ => throw new InvalidOperationException($"Unsupported payment hold status: {Status}.")
    };

    public Result EnsureCanRelease() => Status switch
    {
        PaymentHoldStatus.Held or PaymentHoldStatus.Released => Result.Success(),
        PaymentHoldStatus.Captured => Result.Failure(PaymentHoldErrors.AlreadyCaptured(Id)),
        PaymentHoldStatus.Failed => Result.Failure(PaymentHoldErrors.NotAuthorized(Id)),
        _ => throw new InvalidOperationException($"Unsupported payment hold status: {Status}.")
    };

    public Result Capture(string gatewayTransactionId, DateTimeOffset nowUtc)
    {
        Result guard = EnsureCanCapture();
        if (guard.IsFailure || Status == PaymentHoldStatus.Captured)
        {
            return guard;
        }

        if (string.IsNullOrWhiteSpace(gatewayTransactionId))
        {
            return Result.Failure(PaymentHoldErrors.MissingGatewayReference);
        }

        Status = PaymentHoldStatus.Captured;
        AppendEntry(LedgerEntryType.Capture, gatewayTransactionId, nowUtc);
        IncrementVersion();

        return Result.Success();
    }

    public Result Release(string reason, string gatewayTransactionId, DateTimeOffset nowUtc)
    {
        Result guard = EnsureCanRelease();
        if (guard.IsFailure || Status == PaymentHoldStatus.Released)
        {
            return guard;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(PaymentHoldErrors.MissingReason);
        }

        if (string.IsNullOrWhiteSpace(gatewayTransactionId))
        {
            return Result.Failure(PaymentHoldErrors.MissingGatewayReference);
        }

        Status = PaymentHoldStatus.Released;
        ReleaseReason = NormalizeReason(reason);
        AppendEntry(LedgerEntryType.Release, gatewayTransactionId, nowUtc);
        IncrementVersion();

        return Result.Success();
    }

    // Reasons originate in other services and can be arbitrarily long; truncation keeps a compensating transition
    // from failing on a storage limit, which would strand the customer's held funds.
    private static string NormalizeReason(string reason)
    {
        string trimmed = reason.Trim();
        return trimmed.Length <= MaxReasonLength ? trimmed : trimmed[..MaxReasonLength];
    }

    private void AppendEntry(LedgerEntryType type, string gatewayTransactionId, DateTimeOffset nowUtc)
    {
        string idempotencyKey = IdempotencyKeyFor(OrderId, type);

        // Status guards already prevent repeated operations; reaching this means a transition lost its guard,
        // and silently writing a second money movement would corrupt the ledger.
        if (_ledgerEntries.Exists(entry => string.Equals(entry.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Ledger entry '{idempotencyKey}' has already been recorded.");
        }

        _ledgerEntries.Add(new PaymentLedgerEntry(
            Id,
            type,
            Amount,
            idempotencyKey,
            gatewayTransactionId,
            nowUtc));
    }
}
