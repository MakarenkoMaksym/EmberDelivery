using EmberDelivery.Billing.Api.Domain;
using EmberDelivery.Billing.Api.Domain.PaymentHolds;
using EmberDelivery.Billing.UnitTests.TestUtils;
using EmberDelivery.BuildingBlocks.Domain.Results;

using FluentAssertions;

namespace EmberDelivery.Billing.UnitTests.Domain.PaymentHolds;

public sealed class PaymentHoldTests
{
    private static readonly DateTimeOffset LaterUtc = PaymentHoldTestBuilder.PlacedAtUtc.AddMinutes(30);

    [Fact]
    public void Place_WhenAuthorized_ShouldBeHeldWithSingleAuthorizationEntry()
    {
        PaymentHold hold = PaymentHoldTestBuilder.Held();

        hold.Status.Should().Be(PaymentHoldStatus.Held);
        hold.GatewayAuthorizationId.Should().Be(PaymentHoldTestBuilder.AuthorizationId);
        hold.Version.Should().Be(1);

        PaymentLedgerEntry entry = hold.LedgerEntries.Should().ContainSingle().Subject;
        entry.Type.Should().Be(LedgerEntryType.Authorization);
        entry.PaymentHoldId.Should().Be(hold.Id);
        entry.Amount.Should().Be(PaymentHoldTestBuilder.DefaultMoney);
        entry.GatewayTransactionId.Should().Be(PaymentHoldTestBuilder.AuthorizationId);
        entry.IdempotencyKey.Should().Be(
            PaymentHold.IdempotencyKeyFor(PaymentHoldTestBuilder.OrderId, LedgerEntryType.Authorization));
    }

    [Fact]
    public void Place_WhenAmountIsZero_ShouldFail()
    {
        Money zero = Money.Create(0m, "UAH").Value;

        Result<PaymentHold> result = PaymentHold.Place(
            PaymentHoldTestBuilder.OrderId,
            PaymentHoldTestBuilder.CustomerId,
            zero,
            PaymentHoldTestBuilder.AuthorizationId,
            PaymentHoldTestBuilder.PlacedAtUtc);

        result.Error.Should().Be(PaymentHoldErrors.NonPositiveAmount);
    }

    [Fact]
    public void Place_WhenGatewayReferenceIsMissing_ShouldFail()
    {
        Result<PaymentHold> result = PaymentHold.Place(
            PaymentHoldTestBuilder.OrderId,
            PaymentHoldTestBuilder.CustomerId,
            PaymentHoldTestBuilder.DefaultMoney,
            " ",
            PaymentHoldTestBuilder.PlacedAtUtc);

        result.Error.Should().Be(PaymentHoldErrors.MissingGatewayReference);
    }

    [Fact]
    public void Place_WhenOrderIdIsEmpty_ShouldFail()
    {
        Result<PaymentHold> result = PaymentHold.Place(
            Guid.Empty,
            PaymentHoldTestBuilder.CustomerId,
            PaymentHoldTestBuilder.DefaultMoney,
            PaymentHoldTestBuilder.AuthorizationId,
            PaymentHoldTestBuilder.PlacedAtUtc);

        result.Error.Should().Be(PaymentHoldErrors.MissingIdentifiers);
    }

    [Fact]
    public void Decline_ShouldBeFailedWithoutMovingMoney()
    {
        PaymentHold hold = PaymentHoldTestBuilder.Failed();

        hold.Status.Should().Be(PaymentHoldStatus.Failed);
        hold.FailureReason.Should().Be("Insufficient funds.");
        hold.GatewayAuthorizationId.Should().BeNull();
        hold.LedgerEntries.Should().BeEmpty();
    }

    [Fact]
    public void Decline_WhenReasonExceedsLimit_ShouldTruncateInsteadOfFailing()
    {
        string longReason = new('x', PaymentHold.MaxReasonLength + 50);

        Result<PaymentHold> result = PaymentHold.Decline(
            PaymentHoldTestBuilder.OrderId,
            PaymentHoldTestBuilder.CustomerId,
            PaymentHoldTestBuilder.DefaultMoney,
            longReason,
            PaymentHoldTestBuilder.PlacedAtUtc);

        result.Value.FailureReason.Should().HaveLength(PaymentHold.MaxReasonLength);
    }

    [Fact]
    public void Capture_WhenHeld_ShouldAppendCaptureEntryAndBumpVersion()
    {
        PaymentHold hold = PaymentHoldTestBuilder.Held();

        Result result = hold.Capture(PaymentHoldTestBuilder.CaptureTransactionId, LaterUtc);

        result.IsSuccess.Should().BeTrue();
        hold.Status.Should().Be(PaymentHoldStatus.Captured);
        hold.Version.Should().Be(2);
        hold.LedgerEntries.Select(e => e.Type).Should().Equal(LedgerEntryType.Authorization, LedgerEntryType.Capture);

        PaymentLedgerEntry capture = hold.LedgerEntries.Single(e => e.Type == LedgerEntryType.Capture);
        capture.Amount.Should().Be(PaymentHoldTestBuilder.DefaultMoney);
        capture.OccurredAtUtc.Should().Be(LaterUtc);
        capture.GatewayTransactionId.Should().Be(PaymentHoldTestBuilder.CaptureTransactionId);
    }

    [Fact]
    public void Capture_WhenAlreadyCaptured_ShouldSucceedWithoutSecondEntry()
    {
        PaymentHold hold = PaymentHoldTestBuilder.Captured();
        uint versionBefore = hold.Version;

        Result result = hold.Capture("capt_duplicate", LaterUtc);

        result.IsSuccess.Should().BeTrue();
        hold.Version.Should().Be(versionBefore);
        hold.LedgerEntries.Should().HaveCount(2);
    }

    [Fact]
    public void Capture_WhenReleased_ShouldFailAndLeaveLedgerUntouched()
    {
        PaymentHold hold = PaymentHoldTestBuilder.Released();

        Result result = hold.Capture(PaymentHoldTestBuilder.CaptureTransactionId, LaterUtc);

        result.Error.Should().Be(PaymentHoldErrors.AlreadyReleased(hold.Id));
        hold.Status.Should().Be(PaymentHoldStatus.Released);
        hold.LedgerEntries.Should().NotContain(e => e.Type == LedgerEntryType.Capture);
    }

    [Fact]
    public void Capture_WhenAuthorizationFailed_ShouldFail()
    {
        PaymentHold hold = PaymentHoldTestBuilder.Failed();

        Result result = hold.Capture(PaymentHoldTestBuilder.CaptureTransactionId, LaterUtc);

        result.Error.Should().Be(PaymentHoldErrors.NotAuthorized(hold.Id));
        hold.LedgerEntries.Should().BeEmpty();
    }

    [Fact]
    public void Release_WhenHeld_ShouldAppendReleaseEntryAndStoreReason()
    {
        PaymentHold hold = PaymentHoldTestBuilder.Held();

        Result result = hold.Release("  Customer cancelled.  ", PaymentHoldTestBuilder.ReleaseTransactionId, LaterUtc);

        result.IsSuccess.Should().BeTrue();
        hold.Status.Should().Be(PaymentHoldStatus.Released);
        hold.ReleaseReason.Should().Be("Customer cancelled.");
        hold.Version.Should().Be(2);
        hold.LedgerEntries.Select(e => e.Type).Should().Equal(LedgerEntryType.Authorization, LedgerEntryType.Release);
    }

    [Fact]
    public void Release_WhenAlreadyReleased_ShouldSucceedWithoutSecondEntry()
    {
        PaymentHold hold = PaymentHoldTestBuilder.Released();
        uint versionBefore = hold.Version;

        Result result = hold.Release("Duplicate compensation.", "rel_duplicate", LaterUtc);

        result.IsSuccess.Should().BeTrue();
        hold.Version.Should().Be(versionBefore);
        hold.LedgerEntries.Should().HaveCount(2);
    }

    [Fact]
    public void Release_WhenCaptured_ShouldFail()
    {
        PaymentHold hold = PaymentHoldTestBuilder.Captured();

        Result result = hold.Release("Too late.", PaymentHoldTestBuilder.ReleaseTransactionId, LaterUtc);

        result.Error.Should().Be(PaymentHoldErrors.AlreadyCaptured(hold.Id));
        hold.Status.Should().Be(PaymentHoldStatus.Captured);
    }

    [Fact]
    public void Release_WhenReasonIsMissing_ShouldFailWithoutTransition()
    {
        PaymentHold hold = PaymentHoldTestBuilder.Held();

        Result result = hold.Release(" ", PaymentHoldTestBuilder.ReleaseTransactionId, LaterUtc);

        result.Error.Should().Be(PaymentHoldErrors.MissingReason);
        hold.Status.Should().Be(PaymentHoldStatus.Held);
        hold.Version.Should().Be(1);
    }

    [Fact]
    public void IdempotencyKeyFor_ShouldBeStablePerOrderAndDistinctPerOperation()
    {
        string authorize = PaymentHold.IdempotencyKeyFor(PaymentHoldTestBuilder.OrderId, LedgerEntryType.Authorization);
        string capture = PaymentHold.IdempotencyKeyFor(PaymentHoldTestBuilder.OrderId, LedgerEntryType.Capture);
        string release = PaymentHold.IdempotencyKeyFor(PaymentHoldTestBuilder.OrderId, LedgerEntryType.Release);

        authorize.Should().Be("order-4b1d1a521f0e4f439a3a0d5a3c0f6a11-authorize");
        new[] { authorize, capture, release }.Should().OnlyHaveUniqueItems();
        PaymentHold.IdempotencyKeyFor(PaymentHoldTestBuilder.OrderId, LedgerEntryType.Capture).Should().Be(capture);
    }
}
