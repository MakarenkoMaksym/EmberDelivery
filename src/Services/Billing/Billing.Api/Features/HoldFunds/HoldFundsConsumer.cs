using EmberDelivery.Billing.Api.Domain;
using EmberDelivery.Billing.Api.Domain.PaymentHolds;
using EmberDelivery.Billing.Api.Infrastructure.Messaging;
using EmberDelivery.Billing.Api.Infrastructure.PaymentGateway;
using EmberDelivery.BuildingBlocks.Domain.Results;

using MassTransit;

using Microsoft.EntityFrameworkCore;

using static EmberDelivery.Billing.IntegrationEvents.PaymentContracts;

namespace EmberDelivery.Billing.Api.Features.HoldFunds;

public sealed class HoldFundsConsumer(
    OutboxUnitOfWorkFactory outbox,
    IPaymentGateway paymentGateway,
    TimeProvider timeProvider,
    ILogger<HoldFundsConsumer> logger) : IConsumer<HoldFundsCommand>
{
    public async Task Consume(ConsumeContext<HoldFundsCommand> context)
    {
        HoldFundsCommand command = context.Message;
        CancellationToken cancellationToken = context.CancellationToken;

        // Reads, the gateway call and the final write share this unit of work; EF opens a transaction only inside
        // CommitAsync, so nothing is held open in PostgreSQL while waiting on the provider.
        await using OutboxUnitOfWork unitOfWork = outbox.Begin(context);

        bool holdAlreadyExists = await unitOfWork.DbContext.PaymentHolds
            .AnyAsync(x => x.OrderId == command.OrderId, cancellationToken);

        // The hold row and its outcome event commit in one SaveChanges through the bus outbox, so an existing hold
        // proves the saga has already been answered. Re-publishing would hit sagas that moved past AwaitingPaymentHold
        // and fault there.
        if (holdAlreadyExists)
        {
            logger.LogInformation(
                "Ignoring duplicate HoldFunds for order {OrderId}: a payment hold already exists",
                command.OrderId);
            return;
        }

        Result<Money> amountResult = Money.Create(command.Amount, command.Currency);
        Result amountValidation = amountResult.IsSuccess
            ? PaymentHold.ValidateAmount(amountResult.Value)
            : amountResult;

        // No hold is persisted for a malformed request: rejection is a pure function of the payload, so any replay
        // deterministically produces the same answer without touching the gateway, and the saga ignores the repeat.
        if (amountValidation.IsFailure)
        {
            logger.LogWarning(
                "Rejecting HoldFunds for order {OrderId}: {ErrorCode}",
                command.OrderId,
                amountValidation.Error.Code);

            await unitOfWork.PublishAsync(
                new FundsHoldFailedIntegrationEvent(command.OrderId, amountValidation.Error.Description),
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return;
        }

        Money amount = amountResult.Value;

        PaymentGatewayResult authorization = await paymentGateway.AuthorizeAsync(
            new AuthorizePaymentRequest(
                PaymentHold.IdempotencyKeyFor(command.OrderId, LedgerEntryType.Authorization),
                command.CustomerId,
                amount),
            cancellationToken);

        DateTimeOffset nowUtc = timeProvider.GetUtcNow();

        Result<PaymentHold> holdResult = authorization.IsApproved
            ? PaymentHold.Place(command.OrderId, command.CustomerId, amount, authorization.TransactionId!, nowUtc)
            : PaymentHold.Decline(command.OrderId, command.CustomerId, amount, authorization.DeclineReason!, nowUtc);

        if (holdResult.IsFailure)
        {
            throw new PaymentHoldOperationException(holdResult.Error);
        }

        PaymentHold hold = holdResult.Value;
        unitOfWork.DbContext.PaymentHolds.Add(hold);

        // Never context.Publish here: it would reach the broker immediately, even if the commit below then fails.
        // Through the unit of work a losing duplicate fails on the unique order index, its event is rolled back with
        // the insert, and the retry turns it into a no-op.
        if (hold.Status == PaymentHoldStatus.Held)
        {
            await unitOfWork.PublishAsync(new FundsHeldIntegrationEvent(hold.OrderId, hold.Id), cancellationToken);
        }
        else
        {
            await unitOfWork.PublishAsync(
                new FundsHoldFailedIntegrationEvent(hold.OrderId, hold.FailureReason!),
                cancellationToken);
        }

        await unitOfWork.CommitAsync(cancellationToken);
    }
}
