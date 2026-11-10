using EmberDelivery.BuildingBlocks.Domain.Results;

namespace EmberDelivery.Billing.Api.Features;

// Deliberately excluded from the retry policy: these faults mean the saga and Billing disagree about money, so the
// message goes straight to the error queue for manual reconciliation instead of being retried or silently acked.
public sealed class PaymentHoldOperationException : Exception
{
    public PaymentHoldOperationException()
    {
    }

    public PaymentHoldOperationException(string message)
        : base(message)
    {
    }

    public PaymentHoldOperationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public PaymentHoldOperationException(Error error)
        : base($"{error.Code}: {error.Description}")
    {
    }
}
