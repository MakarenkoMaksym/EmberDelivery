namespace EmberDelivery.Billing.Api.Features.GetPayment;

public sealed record LedgerEntryResponse(
    Guid Id,
    string Type,
    decimal Amount,
    string Currency,
    DateTimeOffset OccurredAtUtc);

public sealed record PaymentResponse(
    Guid PaymentHoldId,
    Guid OrderId,
    Guid CustomerId,
    string Status,
    decimal Amount,
    string Currency,
    string? FailureReason,
    string? ReleaseReason,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<LedgerEntryResponse> LedgerEntries);
