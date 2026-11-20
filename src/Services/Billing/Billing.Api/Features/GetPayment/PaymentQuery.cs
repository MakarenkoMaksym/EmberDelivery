using System.Linq.Expressions;

using EmberDelivery.Billing.Api.Domain.PaymentHolds;
using EmberDelivery.Billing.Api.Infrastructure.Persistence;
using EmberDelivery.BuildingBlocks.Application.Abstractions.Identity;
using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.BuildingBlocks.Infrastructure.Identity;

using Microsoft.EntityFrameworkCore;

namespace EmberDelivery.Billing.Api.Features.GetPayment;

internal static class PaymentQuery
{
    public const string EndpointTag = "Payments";

    public static async Task<Result<PaymentResponse>> FindAsync(
        BillingDbContext dbContext,
        Expression<Func<PaymentHold, bool>> predicate,
        Error notFoundError,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken)
    {
        // Projected in SQL, ledger ordering included, so the aggregate and its ledger are never materialized for a read.
        PaymentResponse? payment = await dbContext.PaymentHolds
            .AsNoTracking()
            .Where(predicate)
            .Select(hold => new PaymentResponse(
                hold.Id,
                hold.OrderId,
                hold.CustomerId,
                hold.Status.ToString(),
                hold.Amount.Amount,
                hold.Amount.Currency,
                hold.FailureReason,
                hold.ReleaseReason,
                hold.CreatedAtUtc,
                hold.LedgerEntries
                    .OrderBy(entry => entry.OccurredAtUtc)
                    .Select(entry => new LedgerEntryResponse(
                        entry.Id,
                        entry.Type.ToString(),
                        entry.Amount.Amount,
                        entry.Amount.Currency,
                        entry.OccurredAtUtc))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);

        if (payment is null)
        {
            return Result.Failure<PaymentResponse>(notFoundError);
        }

        if (!currentUser.IsInRole(Roles.Admin) && payment.CustomerId != currentUser.GetUserId())
        {
            return Result.Failure<PaymentResponse>(PaymentHoldErrors.ForbiddenAccess);
        }

        return Result.Success(payment);
    }
}
