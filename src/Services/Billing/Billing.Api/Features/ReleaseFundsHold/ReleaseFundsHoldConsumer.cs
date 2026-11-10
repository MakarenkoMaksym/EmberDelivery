using EmberDelivery.Billing.Api.Domain.PaymentHolds;
using EmberDelivery.Billing.Api.Infrastructure.PaymentGateway;
using EmberDelivery.Billing.Api.Infrastructure.Persistence;
using EmberDelivery.BuildingBlocks.Domain.Results;

using MassTransit;

using static EmberDelivery.Billing.IntegrationEvents.PaymentContracts;

namespace EmberDelivery.Billing.Api.Features.ReleaseFundsHold;

public sealed class ReleaseFundsHoldConsumer(
    BillingDbContext dbContext,
    IPaymentGateway paymentGateway,
    TimeProvider timeProvider,
    ILogger<ReleaseFundsHoldConsumer> logger) : IConsumer<ReleaseFundsHoldCommand>
{
    public async Task Consume(ConsumeContext<ReleaseFundsHoldCommand> context)
    {
        ReleaseFundsHoldCommand command = context.Message;
        CancellationToken cancellationToken = context.CancellationToken;

        PaymentHold hold = await dbContext.LoadForOrderAsync(
            command.PaymentHoldId,
            command.OrderId,
            cancellationToken);

        if (hold.Status == PaymentHoldStatus.Released)
        {
            logger.LogInformation(
                "Ignoring duplicate ReleaseFundsHold for payment hold {PaymentHoldId}: already released",
                hold.Id);
            return;
        }

        // Checked before the gateway call so captured money is never voided at the provider.
        Result guard = hold.EnsureCanRelease();
        if (guard.IsFailure)
        {
            throw new PaymentHoldOperationException(guard.Error);
        }

        PaymentGatewayResult release = await paymentGateway.ReleaseAsync(
            new ReleasePaymentRequest(
                PaymentHold.IdempotencyKeyFor(hold.OrderId, LedgerEntryType.Release),
                hold.GatewayAuthorizationId!),
            cancellationToken);

        if (!release.IsApproved)
        {
            throw new PaymentHoldOperationException(
                $"Gateway declined release of payment hold '{hold.Id}': {release.DeclineReason}");
        }

        Result released = hold.Release(command.Reason, release.TransactionId!, timeProvider.GetUtcNow());
        if (released.IsFailure)
        {
            throw new PaymentHoldOperationException(released.Error);
        }

        // A concurrent duplicate that committed first bumps the version, so this save fails with a concurrency
        // conflict and the retry observes the final status and exits as a no-op.
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
