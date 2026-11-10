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
        PaymentHold? hold = await dbContext.PaymentHolds
            .AsNoTracking()
            .Include(x => x.LedgerEntries)
            .FirstOrDefaultAsync(predicate, cancellationToken);

        if (hold is null)
        {
            return Result.Failure<PaymentResponse>(notFoundError);
        }

        if (!currentUser.IsInRole(Roles.Admin) && hold.CustomerId != currentUser.GetUserId())
        {
            return Result.Failure<PaymentResponse>(PaymentHoldErrors.ForbiddenAccess);
        }

        return Result.Success(ToResponse(hold));
    }

    private static PaymentResponse ToResponse(PaymentHold hold)
    {
        var ledgerEntries = hold.LedgerEntries
            .OrderBy(entry => entry.OccurredAtUtc)
            .Select(entry => new LedgerEntryResponse(
                entry.Id,
                entry.Type.ToString(),
                entry.Amount.Amount,
                entry.Amount.Currency,
                entry.OccurredAtUtc))
            .ToList();

        return new PaymentResponse(
            hold.Id,
            hold.OrderId,
            hold.CustomerId,
            hold.Status.ToString(),
            hold.Amount.Amount,
            hold.Amount.Currency,
            hold.FailureReason,
            hold.ReleaseReason,
            hold.CreatedAtUtc,
            ledgerEntries);
    }
}
