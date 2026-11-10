using EmberDelivery.Billing.Api.Domain.PaymentHolds;
using EmberDelivery.Billing.Api.Features;
using EmberDelivery.Billing.IntegrationTests.Infrastructure;

using FluentAssertions;

using Xunit;

using static EmberDelivery.Billing.IntegrationEvents.PaymentContracts;

namespace EmberDelivery.Billing.IntegrationTests.Consumers;

public sealed class CaptureFundsConsumerTests(BillingAppFixture fixture) : BaseIntegrationTest(fixture)
{
    [Fact]
    public async Task CaptureFunds_WhenHoldIsHeld_ShouldCaptureAndAppendCaptureLedgerEntry()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        Guid paymentHoldId = await HoldFundsAsync(orderId, Guid.NewGuid());

        // Act
        await PublishAndWaitForSuccessAsync(new CaptureFundsCommand(orderId, paymentHoldId));

        // Assert
        PaymentHold? hold = await FindHoldByOrderAsync(orderId);
        hold.Should().NotBeNull();
        hold.Status.Should().Be(PaymentHoldStatus.Captured);
        hold.Version.Should().Be(2);

        PaymentLedgerEntry capture = hold.LedgerEntries.Should().ContainSingle(e => e.Type == LedgerEntryType.Capture).Subject;
        capture.IdempotencyKey.Should().Be(CaptureKey(orderId));
        capture.Amount.Should().Be(hold.Amount);
        capture.GatewayTransactionId.Should().Be(Gateway.CallsFor(CaptureKey(orderId)).Single().Result!.TransactionId);
    }

    [Fact]
    public async Task CaptureFunds_WhenRedelivered_ShouldNotCallGatewayOrWriteLedgerAgain()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        Guid paymentHoldId = await HoldFundsAsync(orderId, Guid.NewGuid());
        await PublishAndWaitForSuccessAsync(new CaptureFundsCommand(orderId, paymentHoldId));

        // Act
        await PublishAndWaitForSuccessAsync(new CaptureFundsCommand(orderId, paymentHoldId));

        // Assert
        PaymentHold? hold = await FindHoldByOrderAsync(orderId);
        hold!.Status.Should().Be(PaymentHoldStatus.Captured);
        hold.Version.Should().Be(2);
        hold.LedgerEntries.Should().HaveCount(2);
        Gateway.CallsFor(CaptureKey(orderId)).Should().ContainSingle();
    }

    [Fact]
    public async Task CaptureFunds_WhenHoldWasReleased_ShouldFaultWithoutReachingGateway()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        Guid paymentHoldId = await HoldFundsAsync(orderId, Guid.NewGuid());
        await PublishAndWaitForSuccessAsync(new ReleaseFundsHoldCommand(orderId, paymentHoldId, "Kitchen rejected"));

        // Act
        ConsumptionOutcome outcome = await WaitForConsumptionAsync(
            await PublishAsync(new CaptureFundsCommand(orderId, paymentHoldId)));

        // Assert
        outcome.Fault.Should().BeOfType<PaymentHoldOperationException>();
        Gateway.CallsFor(CaptureKey(orderId)).Should().BeEmpty();
        (await FindHoldByOrderAsync(orderId))!.Status.Should().Be(PaymentHoldStatus.Released);
    }

    [Fact]
    public async Task CaptureFunds_WhenHoldBelongsToAnotherOrder_ShouldFaultWithoutReachingGateway()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        Guid paymentHoldId = await HoldFundsAsync(orderId, Guid.NewGuid());
        var foreignOrderId = Guid.NewGuid();

        // Act
        ConsumptionOutcome outcome = await WaitForConsumptionAsync(
            await PublishAsync(new CaptureFundsCommand(foreignOrderId, paymentHoldId)));

        // Assert
        outcome.Fault.Should().BeOfType<PaymentHoldOperationException>();
        Gateway.CallsFor(CaptureKey(orderId)).Should().BeEmpty();
        Gateway.CallsFor(CaptureKey(foreignOrderId)).Should().BeEmpty();
        (await FindHoldByOrderAsync(orderId))!.Status.Should().Be(PaymentHoldStatus.Held);
    }

    [Fact]
    public async Task CaptureFunds_WhileGatewayCallIsInFlight_ShouldNotKeepDatabaseTransactionOpen()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        Guid paymentHoldId = await HoldFundsAsync(orderId, Guid.NewGuid());
        GatewayRendezvous rendezvous = Gateway.ParkUntilAllArrive(participants: 1, GatewayOperation.Capture);

        // Act
        Guid messageId = await PublishAsync(new CaptureFundsCommand(orderId, paymentHoldId));
        await rendezvous.AllArrived.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken);

        long idleInTransaction = await CountIdleInTransactionSessionsAsync();
        rendezvous.Release();

        // Assert
        idleInTransaction.Should().Be(0);
        (await WaitForConsumptionAsync(messageId)).Succeeded.Should().BeTrue();
    }
}
