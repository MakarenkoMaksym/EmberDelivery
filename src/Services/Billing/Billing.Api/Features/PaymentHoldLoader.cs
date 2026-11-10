using EmberDelivery.Billing.Api.Domain.PaymentHolds;
using EmberDelivery.Billing.Api.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace EmberDelivery.Billing.Api.Features;

internal static class PaymentHoldLoader
{
    // FundsHeld is published through the outbox only after the hold commits, so a missing or foreign hold here is
    // never a race to wait out; it is a contract violation that needs human attention.
    public static async Task<PaymentHold> LoadForOrderAsync(
        this BillingDbContext dbContext,
        Guid paymentHoldId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        PaymentHold? hold = await dbContext.PaymentHolds
            .Include(x => x.LedgerEntries)
            .FirstOrDefaultAsync(x => x.Id == paymentHoldId, cancellationToken);

        if (hold is null)
        {
            throw new PaymentHoldOperationException(PaymentHoldErrors.NotFound(paymentHoldId));
        }

        if (hold.OrderId != orderId)
        {
            throw new PaymentHoldOperationException(PaymentHoldErrors.OrderMismatch(paymentHoldId, orderId));
        }

        return hold;
    }
}
