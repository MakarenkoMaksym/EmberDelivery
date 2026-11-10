using EmberDelivery.Billing.Api.Domain.PaymentHolds;
using EmberDelivery.Billing.Api.Features;
using EmberDelivery.Billing.IntegrationTests.Infrastructure;

using FluentAssertions;

using Xunit;

using static EmberDelivery.Billing.IntegrationEvents.PaymentContracts;

namespace EmberDelivery.Billing.IntegrationTests.Consumers;

public sealed class ReleaseFundsHoldConsumerTests(BillingAppFixture fixture) : BaseIntegrationTest(fixture)
{
    [Fact]
    public async Task ReleaseFundsHold_WhenHoldIsHeld_ShouldReleaseWithReasonAndAppendReleaseLedgerEntry()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        Guid paymentHoldId = await HoldFundsAsync(orderId, Guid.NewGuid());

        // Act
        await PublishAndWaitForSuccessAsync(new ReleaseFundsHoldCommand(orderId, paymentHoldId, "  Kitchen rejected  "));

        // Assert
        PaymentHold? hold = await FindHoldByOrderAsync(orderId);
        hold.Should().NotBeNull();
        hold.Status.Should().Be(PaymentHoldStatus.Released);
        hold.ReleaseReason.Should().Be("Kitchen rejected");
        hold.Version.Should().Be(2);

        PaymentLedgerEntry release = hold.LedgerEntries.Should().ContainSingle(e => e.Type == LedgerEntryType.Release).Subject;
        release.IdempotencyKey.Should().Be(ReleaseKey(orderId));
    }

    [Fact]
    public async Task ReleaseFundsHold_WhenRedelivered_ShouldNotCallGatewayOrWriteLedgerAgain()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        Guid paymentHoldId = await HoldFundsAsync(orderId, Guid.NewGuid());
        ReleaseFundsHoldCommand command = new(orderId, paymentHoldId, "Customer cancelled");
        await PublishAndWaitForSuccessAsync(command);

        // Act
        await PublishAndWaitForSuccessAsync(command);

        // Assert
        PaymentHold? hold = await FindHoldByOrderAsync(orderId);
        hold!.Status.Should().Be(PaymentHoldStatus.Released);
        hold.LedgerEntries.Should().HaveCount(2);
        Gateway.CallsFor(ReleaseKey(orderId)).Should().ContainSingle();
    }

    [Fact]
    public async Task ReleaseFundsHold_WhenHoldWasCaptured_ShouldFaultWithoutVoidingCapturedMoney()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        Guid paymentHoldId = await HoldFundsAsync(orderId, Guid.NewGuid());
        await PublishAndWaitForSuccessAsync(new CaptureFundsCommand(orderId, paymentHoldId));

        // Act
        ConsumptionOutcome outcome = await WaitForConsumptionAsync(
            await PublishAsync(new ReleaseFundsHoldCommand(orderId, paymentHoldId, "Too late")));

        // Assert
        outcome.Fault.Should().BeOfType<PaymentHoldOperationException>();
        Gateway.CallsFor(ReleaseKey(orderId)).Should().BeEmpty();

        PaymentHold? hold = await FindHoldByOrderAsync(orderId);
        hold!.Status.Should().Be(PaymentHoldStatus.Captured);
        hold.ReleaseReason.Should().BeNull();
    }

    [Fact]
    public async Task ReleaseFundsHold_WhenGatewayIsTemporarilyUnavailable_ShouldRetryAndReleaseOnce()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        Guid paymentHoldId = await HoldFundsAsync(orderId, Guid.NewGuid());
        Gateway.FailNext(GatewayOperation.Release, times: 2);

        // Act
        await PublishAndWaitForSuccessAsync(new ReleaseFundsHoldCommand(orderId, paymentHoldId, "Restaurant closed"));

        // Assert
        Gateway.CallsFor(ReleaseKey(orderId)).Should().HaveCount(3);

        PaymentHold? hold = await FindHoldByOrderAsync(orderId);
        hold!.Status.Should().Be(PaymentHoldStatus.Released);
        hold.LedgerEntries.Count(e => e.Type == LedgerEntryType.Release).Should().Be(1);
    }
}
