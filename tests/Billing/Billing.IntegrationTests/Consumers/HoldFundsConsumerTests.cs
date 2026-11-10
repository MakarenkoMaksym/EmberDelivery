using EmberDelivery.Billing.Api.Domain.PaymentHolds;
using EmberDelivery.Billing.IntegrationTests.Infrastructure;

using FluentAssertions;

using Xunit;

using static EmberDelivery.Billing.IntegrationEvents.PaymentContracts;

namespace EmberDelivery.Billing.IntegrationTests.Consumers;

public sealed class HoldFundsConsumerTests(BillingAppFixture fixture) : BaseIntegrationTest(fixture)
{
    [Fact]
    public async Task HoldFunds_WhenGatewayApproves_ShouldPersistHeldHoldAndPublishFundsHeld()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        // Act
        Guid paymentHoldId = await HoldFundsAsync(orderId, customerId);

        // Assert
        PaymentHold? hold = await FindHoldByOrderAsync(orderId);
        hold.Should().NotBeNull();
        hold.Id.Should().Be(paymentHoldId);
        hold.CustomerId.Should().Be(customerId);
        hold.Status.Should().Be(PaymentHoldStatus.Held);
        hold.Amount.Amount.Should().Be(StandardAmount);
        hold.Amount.Currency.Should().Be(Currency);

        PaymentLedgerEntry authorization = hold.LedgerEntries.Should().ContainSingle().Subject;
        authorization.Type.Should().Be(LedgerEntryType.Authorization);
        authorization.IdempotencyKey.Should().Be(AuthorizeKey(orderId));
        authorization.GatewayTransactionId.Should().Be(hold.GatewayAuthorizationId);

        Gateway.CallsFor(AuthorizeKey(orderId)).Should().ContainSingle();
    }

    [Fact]
    public async Task HoldFunds_WhenGatewayDeclines_ShouldPersistFailedHoldWithoutLedgerAndPublishFundsHoldFailed()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        decimal amountAboveLimit = BillingAppFixture.DeclineAmountAbove + 1m;

        // Act
        await PublishAsync(new HoldFundsCommand(orderId, Guid.NewGuid(), amountAboveLimit, Currency));
        await WaitUntilAsync(() => Task.FromResult(Outcomes.FundsHoldFailedFor(orderId).Count > 0), "FundsHoldFailed");

        // Assert
        Outcomes.FundsHoldFailedFor(orderId).Single().Reason.Should().Be("Insufficient funds.");

        PaymentHold? hold = await FindHoldByOrderAsync(orderId);
        hold.Should().NotBeNull();
        hold.Status.Should().Be(PaymentHoldStatus.Failed);
        hold.FailureReason.Should().Be("Insufficient funds.");
        hold.GatewayAuthorizationId.Should().BeNull();
        hold.LedgerEntries.Should().BeEmpty();
    }

    [Theory]
    [InlineData(10.001, "UAH")]
    [InlineData(0, "UAH")]
    [InlineData(100, "HRYVNIA")]
    public async Task HoldFunds_WhenPayloadIsInvalid_ShouldRejectWithoutCallingGatewayOrPersistingHold(
        double amount,
        string currency)
    {
        // Arrange
        var orderId = Guid.NewGuid();

        // Act
        await PublishAsync(new HoldFundsCommand(orderId, Guid.NewGuid(), (decimal)amount, currency));
        await WaitUntilAsync(() => Task.FromResult(Outcomes.FundsHoldFailedFor(orderId).Count > 0), "FundsHoldFailed");

        // Assert
        (await CountHoldsAsync(orderId)).Should().Be(0);
        Gateway.CallsFor(AuthorizeKey(orderId)).Should().BeEmpty();
    }

    [Fact]
    public async Task HoldFunds_WhenRedeliveredWithNewMessageId_ShouldNotAuthorizeAgainOrRepublishOutcome()
    {
        // Arrange
        // A different MessageId is what an outbox replay or a saga re-send produces; only the OrderId ties them together.
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        Guid paymentHoldId = await HoldFundsAsync(orderId, customerId);

        // Act
        await PublishAndWaitForSuccessAsync(new HoldFundsCommand(orderId, customerId, StandardAmount, Currency));
        await WaitForOutboxDrainedAsync();

        // Assert
        (await CountHoldsAsync(orderId)).Should().Be(1);
        Outcomes.FundsHeldFor(orderId).Should().ContainSingle().Which.PaymentHoldId.Should().Be(paymentHoldId);
        Outcomes.FundsHoldFailedFor(orderId).Should().BeEmpty();
        Gateway.CallsFor(AuthorizeKey(orderId)).Should().ContainSingle();
    }

    [Fact]
    public async Task HoldFunds_WhenRedeliveredAfterDecline_ShouldNotRetryAuthorizationOnCustomerCard()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        HoldFundsCommand command = new(orderId, Guid.NewGuid(), BillingAppFixture.DeclineAmountAbove + 1m, Currency);
        await PublishAndWaitForSuccessAsync(command);

        // Act
        await PublishAndWaitForSuccessAsync(command);
        await WaitForOutboxDrainedAsync();

        // Assert
        (await CountHoldsAsync(orderId)).Should().Be(1);
        Outcomes.FundsHoldFailedFor(orderId).Should().ContainSingle();
        Gateway.CallsFor(AuthorizeKey(orderId)).Should().ContainSingle();
    }

    [Fact]
    public async Task HoldFunds_WhenGatewayIsTemporarilyUnavailable_ShouldRetryWithSameIdempotencyKeyAndHoldOnce()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        Gateway.FailNext(GatewayOperation.Authorize, times: 2);

        // Act
        await HoldFundsAsync(orderId, Guid.NewGuid());
        await WaitForOutboxDrainedAsync();

        // Assert
        IReadOnlyList<GatewayCall> calls = Gateway.CallsFor(AuthorizeKey(orderId));
        calls.Should().HaveCount(3);
        calls.Count(call => call.Result is not null).Should().Be(1);

        (await CountHoldsAsync(orderId)).Should().Be(1);
        Outcomes.FundsHeldFor(orderId).Should().ContainSingle();
    }

    [Fact]
    public async Task HoldFunds_WhenCommitIsLostAfterGatewayApproval_ShouldReuseProviderAuthorizationAndPublishOnlyCommittedHold()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        Fixture.FaultInjector.FailNextPaymentHoldSave();

        // Act
        Guid publishedHoldId = await HoldFundsAsync(orderId, Guid.NewGuid());
        await WaitForOutboxDrainedAsync();

        // Assert
        // The retry re-authorizes with the same key and the provider replays its original answer, so the customer
        // ends up with exactly one authorization even though Billing called the gateway twice.
        IReadOnlyList<GatewayCall> calls = Gateway.CallsFor(AuthorizeKey(orderId));
        calls.Should().HaveCount(2);
        calls.Select(call => call.Result!.TransactionId).Distinct().Should().ContainSingle();

        PaymentHold? hold = await FindHoldByOrderAsync(orderId);
        hold.Should().NotBeNull();
        hold.GatewayAuthorizationId.Should().Be(calls[0].Result!.TransactionId);

        // The failed attempt's FundsHeld referenced a hold id that was never committed. Seeing only the committed id
        // proves the event travelled through the bus outbox in the same transaction, not straight to the broker.
        Outcomes.FundsHeldFor(orderId).Should().ContainSingle();
        publishedHoldId.Should().Be(hold.Id);
    }

    [Fact]
    public async Task HoldFunds_WhileGatewayCallIsInFlight_ShouldNotKeepDatabaseTransactionOpen()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        GatewayRendezvous rendezvous = Gateway.ParkUntilAllArrive(participants: 1, GatewayOperation.Authorize);

        // Act
        await PublishAsync(new HoldFundsCommand(orderId, Guid.NewGuid(), StandardAmount, Currency));
        await rendezvous.AllArrived.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken);

        long idleInTransaction = await CountIdleInTransactionSessionsAsync();
        rendezvous.Release();

        // Assert
        // With the consumer outbox, the InboxState row would be inserted inside a transaction opened before Consume,
        // leaving the session "idle in transaction" for the whole provider round-trip.
        idleInTransaction.Should().Be(0);

        await WaitUntilAsync(() => Task.FromResult(Outcomes.FundsHeldFor(orderId).Count > 0), "FundsHeld");
    }
}
