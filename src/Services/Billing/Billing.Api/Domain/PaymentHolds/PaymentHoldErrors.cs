using EmberDelivery.BuildingBlocks.Domain.Results;

namespace EmberDelivery.Billing.Api.Domain.PaymentHolds;

public static class PaymentHoldErrors
{
    public static readonly Error MissingIdentifiers = Error.Validation(
        "PaymentHold.MissingIdentifiers",
        "Payment hold requires non-empty order and customer identifiers.");

    public static readonly Error NonPositiveAmount = Error.Validation(
        "PaymentHold.NonPositiveAmount",
        "Payment hold amount must be greater than zero.");

    public static readonly Error MissingGatewayReference = Error.Validation(
        "PaymentHold.MissingGatewayReference",
        "Payment gateway did not return a transaction reference.");

    public static readonly Error MissingReason = Error.Validation(
        "PaymentHold.MissingReason",
        "A reason is required for this payment hold transition.");

    public static readonly Error ForbiddenAccess = Error.Forbidden(
        "PaymentHold.ForbiddenAccess",
        "You do not have permission to view this payment.");

    public static Error NotFound(Guid paymentHoldId) => Error.NotFound(
        "PaymentHold.NotFound",
        $"Payment hold '{paymentHoldId}' was not found.");

    public static Error NotFoundForOrder(Guid orderId) => Error.NotFound(
        "PaymentHold.NotFoundForOrder",
        $"No payment hold exists for order '{orderId}'.");

    public static Error OrderMismatch(Guid paymentHoldId, Guid orderId) => Error.Conflict(
        "PaymentHold.OrderMismatch",
        $"Payment hold '{paymentHoldId}' does not belong to order '{orderId}'.");

    public static Error AlreadyCaptured(Guid paymentHoldId) => Error.Conflict(
        "PaymentHold.AlreadyCaptured",
        $"Payment hold '{paymentHoldId}' has already been captured and cannot be released.");

    public static Error AlreadyReleased(Guid paymentHoldId) => Error.Conflict(
        "PaymentHold.AlreadyReleased",
        $"Payment hold '{paymentHoldId}' has already been released and cannot be captured.");

    public static Error NotAuthorized(Guid paymentHoldId) => Error.Conflict(
        "PaymentHold.NotAuthorized",
        $"Payment hold '{paymentHoldId}' was never authorized, so no funds can be moved.");
}
