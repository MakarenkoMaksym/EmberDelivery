using EmberDelivery.Billing.Api.Domain.PaymentHolds;
using EmberDelivery.Billing.Api.Infrastructure.PaymentGateway;
using EmberDelivery.Billing.Api.Infrastructure.Persistence;
using EmberDelivery.BuildingBlocks.Domain.Results;

using MassTransit;

using static EmberDelivery.Billing.IntegrationEvents.PaymentContracts;

namespace EmberDelivery.Billing.Api.Features.CaptureFunds;

public sealed class CaptureFundsConsumer(
    BillingDbContext dbContext,
    IPaymentGateway paymentGateway,
    TimeProvider timeProvider,
    ILogger<CaptureFundsConsumer> logger) : IConsumer<CaptureFundsCommand>
{
    public async Task Consume(ConsumeContext<CaptureFundsCommand> context)
    {
        CaptureFundsCommand command = context.Message;
        CancellationToken cancellationToken = context.CancellationToken;

        PaymentHold hold = await dbContext.LoadForOrderAsync(
            command.PaymentHoldId,
            command.OrderId,
            cancellationToken);

        if (hold.Status == PaymentHoldStatus.Captured)
        {
            logger.LogInformation(
                "Ignoring duplicate CaptureFunds for payment hold {PaymentHoldId}: already captured",
                hold.Id);
            return;
        }

        // Checked before the gateway call so an illegal capture never reaches the provider.
        Result guard = hold.EnsureCanCapture();
        if (guard.IsFailure)
        {
            throw new PaymentHoldOperationException(guard.Error);
        }

        PaymentGatewayResult capture = await paymentGateway.CaptureAsync(
            new CapturePaymentRequest(
                PaymentHold.IdempotencyKeyFor(hold.OrderId, LedgerEntryType.Capture),
                hold.GatewayAuthorizationId!,
                hold.Amount),
            cancellationToken);

        if (!capture.IsApproved)
        {
            throw new PaymentHoldOperationException(
                $"Gateway declined capture of payment hold '{hold.Id}': {capture.DeclineReason}");
        }

        Result captured = hold.Capture(capture.TransactionId!, timeProvider.GetUtcNow());
        if (captured.IsFailure)
        {
            throw new PaymentHoldOperationException(captured.Error);
        }

        // A concurrent duplicate that committed first bumps the version, so this save fails with a concurrency
        // conflict and the retry observes the final status and exits as a no-op.
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
