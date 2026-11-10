using EmberDelivery.Billing.Api.Domain.PaymentHolds;
using EmberDelivery.Billing.Api.Features;
using EmberDelivery.Billing.IntegrationTests.Infrastructure;

using FluentAssertions;

using Xunit;

using static EmberDelivery.Billing.IntegrationEvents.PaymentContracts;

namespace EmberDelivery.Billing.IntegrationTests.Concurrency;

// Every race is forced deterministically: both deliveries are parked at the gateway after passing their read-side
// guards, so both are guaranteed to reach the write with the same stale view. Only the database constraints
// (unique order, unique ledger key, version token) and the retry policy stand between them and double charging.
public sealed class PaymentHoldRaceTests(BillingAppFixture fixture) : BaseIntegrationTest(fixture)
{
    private static readonly TimeSpan RendezvousTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task HoldFunds_WhenTwoDuplicatesRacePastExistenceCheck_ShouldCommitOneHoldAndPublishOneOutcome()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        HoldFundsCommand command = new(orderId, Guid.NewGuid(), StandardAmount, Currency);
        GatewayRendezvous rendezvous = Gateway.ParkUntilAllArrive(participants: 2, GatewayOperation.Authorize);

        // Act
        Guid firstMessageId = await PublishAsync(command);
        Guid secondMessageId = await PublishAsync(command);

        await rendezvous.AllArrived.WaitAsync(RendezvousTimeout, CancellationToken);
        long idleInTransaction = await CountIdleInTransactionSessionsAsync();
        rendezvous.Release();

        ConsumptionOutcome first = await WaitForConsumptionAsync(firstMessageId);
        ConsumptionOutcome second = await WaitForConsumptionAsync(secondMessageId);
        await WaitForOutboxDrainedAsync();

        // Assert
        first.Succeeded.Should().BeTrue(first.Fault?.ToString());
        second.Succeeded.Should().BeTrue(second.Fault?.ToString());
        idleInTransaction.Should().Be(0, "neither parked consumer may pin a transaction during the provider call");

        // Both reached the provider, which deduplicated them by idempotency key into one authorization.
        IReadOnlyList<GatewayCall> calls = Gateway.CallsFor(AuthorizeKey(orderId));
        calls.Should().HaveCount(2);
        calls.Select(call => call.Result!.TransactionId).Distinct().Should().ContainSingle();

        PaymentHold? hold = await FindHoldByOrderAsync(orderId);
        hold.Should().NotBeNull();
        hold.LedgerEntries.Should().ContainSingle();
        (await CountHoldsAsync(orderId)).Should().Be(1);

        // The loser's FundsHeld was rolled back together with its rejected insert.
        Outcomes.FundsHeldFor(orderId).Should().ContainSingle().Which.PaymentHoldId.Should().Be(hold.Id);
        Outcomes.FundsHoldFailedFor(orderId).Should().BeEmpty();
    }

    [Fact]
    public async Task CaptureFunds_WhenTwoDuplicatesRace_ShouldRecordSingleCaptureAndBumpVersionOnce()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        Guid paymentHoldId = await HoldFundsAsync(orderId, Guid.NewGuid());
        GatewayRendezvous rendezvous = Gateway.ParkUntilAllArrive(participants: 2, GatewayOperation.Capture);

        // Act
        Guid firstMessageId = await PublishAsync(new CaptureFundsCommand(orderId, paymentHoldId));
        Guid secondMessageId = await PublishAsync(new CaptureFundsCommand(orderId, paymentHoldId));

        await rendezvous.AllArrived.WaitAsync(RendezvousTimeout, CancellationToken);
        rendezvous.Release();

        ConsumptionOutcome first = await WaitForConsumptionAsync(firstMessageId);
        ConsumptionOutcome second = await WaitForConsumptionAsync(secondMessageId);

        // Assert
        // The loser fails on the version token or the unique ledger key, retries, sees Captured and exits as a no-op.
        first.Succeeded.Should().BeTrue(first.Fault?.ToString());
        second.Succeeded.Should().BeTrue(second.Fault?.ToString());

        PaymentHold? hold = await FindHoldByOrderAsync(orderId);
        hold!.Status.Should().Be(PaymentHoldStatus.Captured);
        hold.Version.Should().Be(2);
        hold.LedgerEntries.Should().ContainSingle(e => e.Type == LedgerEntryType.Capture)
            .Which.GatewayTransactionId.Should().Be(Gateway.CallsFor(CaptureKey(orderId))[0].Result!.TransactionId);
    }

    [Fact]
    public async Task CaptureAndRelease_WhenRacingOnSameHold_ShouldLetExactlyOneTransitionWinAndFaultTheOther()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        Guid paymentHoldId = await HoldFundsAsync(orderId, Guid.NewGuid());
        GatewayRendezvous rendezvous = Gateway.ParkUntilAllArrive(
            participants: 2,
            GatewayOperation.Capture,
            GatewayOperation.Release);

        // Act
        Guid captureMessageId = await PublishAsync(new CaptureFundsCommand(orderId, paymentHoldId));
        Guid releaseMessageId = await PublishAsync(new ReleaseFundsHoldCommand(orderId, paymentHoldId, "Customer cancelled"));

        await rendezvous.AllArrived.WaitAsync(RendezvousTimeout, CancellationToken);
        rendezvous.Release();

        ConsumptionOutcome capture = await WaitForConsumptionAsync(captureMessageId);
        ConsumptionOutcome release = await WaitForConsumptionAsync(releaseMessageId);

        // Assert
        new[] { capture, release }.Count(outcome => outcome.Succeeded).Should().Be(1);
        new[] { capture, release }.Single(outcome => !outcome.Succeeded)
            .Fault.Should().BeOfType<PaymentHoldOperationException>("the loser must land in the error queue for reconciliation");

        PaymentHold? hold = await FindHoldByOrderAsync(orderId);
        hold.Should().NotBeNull();

        PaymentHoldStatus expectedStatus = capture.Succeeded ? PaymentHoldStatus.Captured : PaymentHoldStatus.Released;
        LedgerEntryType expectedEntry = capture.Succeeded ? LedgerEntryType.Capture : LedgerEntryType.Release;

        hold.Status.Should().Be(expectedStatus);
        hold.Version.Should().Be(2);
        hold.LedgerEntries.Select(e => e.Type).Should().BeEquivalentTo([LedgerEntryType.Authorization, expectedEntry]);
    }
}
