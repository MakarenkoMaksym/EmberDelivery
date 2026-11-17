using EmberDelivery.Ordering.Application.Sagas.OrderFulfillment;
using EmberDelivery.Ordering.IntegrationEvents;

using FluentAssertions;

using MassTransit;
using MassTransit.Testing;

using Microsoft.Extensions.DependencyInjection;

using static EmberDelivery.Billing.IntegrationEvents.PaymentContracts;
using static EmberDelivery.Dispatch.IntegrationEvents.CourierContracts;
using static EmberDelivery.Kitchen.IntegrationEvents.KitchenSlotContracts;
using static EmberDelivery.Ordering.IntegrationEvents.OrderSagaLifecycleEvents;
using static EmberDelivery.Ordering.IntegrationEvents.OrderSagaTimeouts;

namespace EmberDelivery.Ordering.UnitTests.Application.Sagas;

public sealed class OrderFulfillmentStateMachineTests
{
    private const decimal TotalAmount = 450m;
    private const string Currency = "UAH";
    private const int EstimatedPrepMinutes = 20;

    [Fact]
    public async Task OrderSubmitted_ShouldInitializeSagaInAwaitingPaymentHoldAndPublishHoldFundsCommand()
    {
        // Arrange
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await scenario.SubmitAsync();

        // Assert
        OrderSagaState instance = await scenario.ShouldBeInStateAsync(x => x.AwaitingPaymentHold);
        instance.TotalAmount.Should().Be(TotalAmount);
        instance.PaymentHoldTimeoutTokenId.Should().NotBeNull();

        (await scenario.WasPublishedAsync<HoldFundsCommand>(x => x.OrderId == scenario.OrderId && x.Amount == TotalAmount))
            .Should().BeTrue();
    }

    [Fact]
    public async Task OrderSubmitted_WhenRedelivered_ShouldBeIgnoredWithoutFaultingOrRepeatingHoldFundsCommand()
    {
        // Arrange
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        await scenario.SubmitAsync();

        // Act
        await scenario.SubmitAsync();

        // Assert
        await scenario.ShouldBeInStateAsync(x => x.AwaitingPaymentHold);
        scenario.ShouldHaveNoFaults();
        scenario.PublishedCount<HoldFundsCommand>().Should().Be(1);
    }

    [Fact]
    public async Task FundsHoldFailed_ShouldPublishFulfillmentFailedAndTransitionToCancelled()
    {
        // Arrange
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        await scenario.SubmitAsync();

        // Act
        await scenario.PublishAsync(new FundsHoldFailedIntegrationEvent(scenario.OrderId, "Card declined"));

        // Assert
        OrderSagaState instance = await scenario.ShouldBeInStateAsync(x => x.Cancelled);
        instance.FailureReason.Should().Be("Card declined");
        instance.PaymentHoldTimeoutTokenId.Should().BeNull();

        (await scenario.WasPublishedAsync<OrderFulfillmentFailedIntegrationEvent>(x => x.OrderId == scenario.OrderId))
            .Should().BeTrue();
    }

    [Fact]
    public async Task PaymentHoldTimeout_WhenBillingDoesNotRespond_ShouldFailOrderAndReleaseHoldThatArrivesLater()
    {
        // Arrange
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        await scenario.SubmitAsync();
        var latePaymentHoldId = Guid.NewGuid();

        // Act
        await scenario.PublishAsync(new PaymentHoldTimeoutExpired(scenario.OrderId));
        await scenario.PublishAsync(new FundsHeldIntegrationEvent(scenario.OrderId, latePaymentHoldId));

        // Assert
        OrderSagaState instance = await scenario.ShouldBeInStateAsync(x => x.Cancelled);
        instance.FailureReason.Should().Be(OrderFulfillmentStateMachine.PaymentHoldTimedOutReason);

        (await scenario.WasPublishedAsync<OrderFulfillmentFailedIntegrationEvent>(
            x => x.OrderId == scenario.OrderId && x.Reason == OrderFulfillmentStateMachine.PaymentHoldTimedOutReason))
            .Should().BeTrue();

        (await scenario.WasPublishedAsync<ReleaseFundsHoldCommand>(
            x => x.OrderId == scenario.OrderId && x.PaymentHoldId == latePaymentHoldId))
            .Should().BeTrue();
    }

    [Fact]
    public async Task FundsHeld_ShouldConfirmPaymentRequestKitchenCapacityAndArmKitchenTimeout()
    {
        // Arrange
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        await scenario.SubmitAsync();

        // Act
        await scenario.HoldFundsAsync();

        // Assert
        OrderSagaState instance = await scenario.ShouldBeInStateAsync(x => x.AwaitingKitchenConfirmation);
        instance.PaymentHoldId.Should().Be(scenario.PaymentHoldId);
        instance.PaymentHoldTimeoutTokenId.Should().BeNull();
        instance.KitchenConfirmationTimeoutTokenId.Should().NotBeNull();

        (await scenario.WasPublishedAsync<OrderPaymentConfirmedIntegrationEvent>(x => x.OrderId == scenario.OrderId))
            .Should().BeTrue();
        (await scenario.WasPublishedAsync<ConfirmKitchenCapacityCommand>(x => x.OrderId == scenario.OrderId))
            .Should().BeTrue();
    }

    [Fact]
    public async Task KitchenRejected_WhenPaymentWasAlreadyHeld_ShouldPublishReleaseFundsAndTransitionToCancelled()
    {
        // Arrange
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        await scenario.SubmitAsync();
        await scenario.HoldFundsAsync();

        // Act: Kitchen rejects the order because salmon is out of stock.
        await scenario.PublishAsync(new KitchenRejectedOrderIntegrationEvent(scenario.OrderId, "Salmon out of stock"));

        // Assert
        OrderSagaState instance = await scenario.ShouldBeInStateAsync(x => x.Cancelled);
        instance.FailureReason.Should().Be("Salmon out of stock");
        instance.KitchenConfirmationTimeoutTokenId.Should().BeNull();

        await scenario.ShouldHaveReleasedHoldAsync();
        (await scenario.WasPublishedAsync<OrderFulfillmentFailedIntegrationEvent>(x => x.OrderId == scenario.OrderId))
            .Should().BeTrue();
    }

    [Fact]
    public async Task KitchenConfirmationTimeout_WhenKitchenDoesNotRespond_ShouldReleaseFundsAndFailOrder()
    {
        // Arrange
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        await scenario.SubmitAsync();
        await scenario.HoldFundsAsync();

        // Act
        await scenario.PublishAsync(new KitchenConfirmationTimeoutExpired(scenario.OrderId));

        // Assert
        OrderSagaState instance = await scenario.ShouldBeInStateAsync(x => x.Cancelled);
        instance.FailureReason.Should().Be(OrderFulfillmentStateMachine.KitchenConfirmationTimedOutReason);

        await scenario.ShouldHaveReleasedHoldAsync();
        (await scenario.WasPublishedAsync<OrderFulfillmentFailedIntegrationEvent>(
            x => x.OrderId == scenario.OrderId && x.Reason == OrderFulfillmentStateMachine.KitchenConfirmationTimedOutReason))
            .Should().BeTrue();
    }

    [Fact]
    public async Task OrderCancelledByCustomer_WhenInAwaitingKitchenConfirmation_ShouldPublishReleaseFundsAndTransitionToCancelled()
    {
        // Arrange
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        await scenario.SubmitAsync();
        await scenario.HoldFundsAsync();

        // Act: Customer cancels the order while it is Paid and awaiting kitchen confirmation.
        await scenario.PublishAsync(new OrderCancelledByCustomerIntegrationEvent(scenario.OrderId, "Changed my mind"));

        // Assert
        OrderSagaState instance = await scenario.ShouldBeInStateAsync(x => x.Cancelled);
        instance.FailureReason.Should().Be("Changed my mind");

        await scenario.ShouldHaveReleasedHoldAsync();
    }

    [Fact]
    public async Task KitchenAccepted_WhenArrivesAfterCustomerAlreadyCancelled_ShouldCommandKitchenToCancelAndStayCancelled()
    {
        // Arrange
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        await scenario.SubmitAsync();
        await scenario.HoldFundsAsync();
        await scenario.PublishAsync(new OrderCancelledByCustomerIntegrationEvent(scenario.OrderId, "Changed my mind"));

        // Act: the restaurant accepted the order before the cancellation could reach it.
        await scenario.PublishAsync(new OrderAcceptedByKitchenIntegrationEvent(scenario.OrderId, EstimatedPrepMinutes));

        // Assert
        await scenario.ShouldBeInStateAsync(x => x.Cancelled);
        scenario.ShouldHaveNoFaults();

        (await scenario.WasPublishedAsync<CancelKitchenOrderCommand>(
            x => x.OrderId == scenario.OrderId && x.Reason == "Changed my mind"))
            .Should().BeTrue();
        scenario.PublishedCount<OrderCookingStartedIntegrationEvent>().Should().Be(0);
    }

    [Fact]
    public async Task KitchenAccepted_ShouldStartCookingAndArmCourierDispatchSchedule()
    {
        // Arrange
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        await scenario.SubmitAsync();
        await scenario.HoldFundsAsync();

        // Act
        await scenario.AcceptByKitchenAsync();

        // Assert
        OrderSagaState instance = await scenario.ShouldBeInStateAsync(x => x.CookingWaitingForDispatchWindow);
        instance.KitchenConfirmationTimeoutTokenId.Should().BeNull();
        instance.CourierDispatchScheduleTokenId.Should().NotBeNull();

        (await scenario.WasPublishedAsync<OrderCookingStartedIntegrationEvent>(
            x => x.OrderId == scenario.OrderId && x.EstimatedPrepMinutes == EstimatedPrepMinutes))
            .Should().BeTrue();
    }

    [Fact]
    public async Task KitchenRejected_WhenCooking_ShouldCancelDispatchScheduleReleaseFundsAndFailOrder()
    {
        // Arrange
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        await scenario.SubmitAsync();
        await scenario.HoldFundsAsync();
        await scenario.AcceptByKitchenAsync();

        // Act
        await scenario.PublishAsync(new KitchenRejectedOrderIntegrationEvent(scenario.OrderId, "Oven broke down"));

        // Assert
        OrderSagaState instance = await scenario.ShouldBeInStateAsync(x => x.Cancelled);
        instance.CourierDispatchScheduleTokenId.Should().BeNull();

        await scenario.ShouldHaveReleasedHoldAsync();
        (await scenario.WasPublishedAsync<OrderFulfillmentFailedIntegrationEvent>(x => x.OrderId == scenario.OrderId))
            .Should().BeTrue();
    }

    [Fact]
    public async Task OrderCancelledByCustomer_WhenCooking_ShouldHonourCancellationByStoppingKitchenAndReleasingFunds()
    {
        // Arrange: the order aggregate accepted the cancellation while Paid, but the event reached the saga late.
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        await scenario.SubmitAsync();
        await scenario.HoldFundsAsync();
        await scenario.AcceptByKitchenAsync();

        // Act
        await scenario.PublishAsync(new OrderCancelledByCustomerIntegrationEvent(scenario.OrderId, "Changed my mind"));

        // Assert
        OrderSagaState instance = await scenario.ShouldBeInStateAsync(x => x.Cancelled);
        instance.CourierDispatchScheduleTokenId.Should().BeNull();

        await scenario.ShouldHaveReleasedHoldAsync();
        (await scenario.WasPublishedAsync<CancelKitchenOrderCommand>(x => x.OrderId == scenario.OrderId))
            .Should().BeTrue();
    }

    [Fact]
    public async Task CourierDispatchTimer_WhenFired_ShouldStartCourierMatchingAndArmPickupTimeout()
    {
        // Arrange
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        await scenario.SubmitAsync();
        await scenario.HoldFundsAsync();
        await scenario.AcceptByKitchenAsync();

        // Act
        await scenario.TriggerCourierDispatchAsync();

        // Assert
        OrderSagaState instance = await scenario.ShouldBeInStateAsync(x => x.MatchingCourier);
        instance.CourierPickupTimeoutTokenId.Should().NotBeNull();

        (await scenario.WasPublishedAsync<StartCourierMatchingCommand>(x => x.OrderId == scenario.OrderId))
            .Should().BeTrue();
    }

    [Fact]
    public async Task CourierMatchingFailed_ShouldStopKitchenReleaseFundsAndFailOrder()
    {
        // Arrange
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        await scenario.ReachMatchingCourierAsync();

        // Act
        await scenario.PublishAsync(new CourierMatchingFailedIntegrationEvent(scenario.OrderId, "No couriers nearby"));

        // Assert
        OrderSagaState instance = await scenario.ShouldBeInStateAsync(x => x.Cancelled);
        instance.FailureReason.Should().Be("No couriers nearby");
        instance.CourierPickupTimeoutTokenId.Should().BeNull();

        await scenario.ShouldHaveReleasedHoldAsync();
        (await scenario.WasPublishedAsync<CancelKitchenOrderCommand>(x => x.OrderId == scenario.OrderId))
            .Should().BeTrue();
        (await scenario.WasPublishedAsync<OrderFulfillmentFailedIntegrationEvent>(x => x.OrderId == scenario.OrderId))
            .Should().BeTrue();
    }

    [Fact]
    public async Task CourierPickupTimeout_WhenNobodyPicksUpOrder_ShouldCancelDeliveryStopKitchenReleaseFundsAndFailOrder()
    {
        // Arrange
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        await scenario.ReachMatchingCourierAsync();

        // Act
        await scenario.PublishAsync(new CourierPickupTimeoutExpired(scenario.OrderId));

        // Assert
        OrderSagaState instance = await scenario.ShouldBeInStateAsync(x => x.Cancelled);
        instance.FailureReason.Should().Be(OrderFulfillmentStateMachine.CourierPickupTimedOutReason);

        await scenario.ShouldHaveReleasedHoldAsync();
        (await scenario.WasPublishedAsync<CancelDeliveryCommand>(x => x.OrderId == scenario.OrderId))
            .Should().BeTrue();
        (await scenario.WasPublishedAsync<CancelKitchenOrderCommand>(x => x.OrderId == scenario.OrderId))
            .Should().BeTrue();
        (await scenario.WasPublishedAsync<OrderFulfillmentFailedIntegrationEvent>(
            x => x.OrderId == scenario.OrderId && x.Reason == OrderFulfillmentStateMachine.CourierPickupTimedOutReason))
            .Should().BeTrue();
    }

    [Fact]
    public async Task KitchenRejected_WhenMatchingCourier_ShouldCancelDeliveryReleaseFundsAndFailOrder()
    {
        // Arrange
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        await scenario.ReachMatchingCourierAsync();

        // Act
        await scenario.PublishAsync(new KitchenRejectedOrderIntegrationEvent(scenario.OrderId, "Dish burned"));

        // Assert
        await scenario.ShouldBeInStateAsync(x => x.Cancelled);

        await scenario.ShouldHaveReleasedHoldAsync();
        (await scenario.WasPublishedAsync<CancelDeliveryCommand>(x => x.OrderId == scenario.OrderId))
            .Should().BeTrue();
        (await scenario.WasPublishedAsync<OrderFulfillmentFailedIntegrationEvent>(x => x.OrderId == scenario.OrderId))
            .Should().BeTrue();
    }

    [Fact]
    public async Task OrderCancelledByCustomer_WhenDelivering_ShouldCancelDeliveryAndReleaseFundsInsteadOfCapturing()
    {
        // Arrange
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        await scenario.ReachMatchingCourierAsync();
        await scenario.PublishAsync(new OrderPickedUpIntegrationEvent(scenario.OrderId, scenario.CourierId));

        // Act
        await scenario.PublishAsync(new OrderCancelledByCustomerIntegrationEvent(scenario.OrderId, "Changed my mind"));
        await scenario.PublishAsync(new OrderDeliveredIntegrationEvent(scenario.OrderId, scenario.CourierId));

        // Assert
        await scenario.ShouldBeInStateAsync(x => x.Cancelled);
        scenario.ShouldHaveNoFaults();

        await scenario.ShouldHaveReleasedHoldAsync();
        (await scenario.WasPublishedAsync<CancelDeliveryCommand>(x => x.OrderId == scenario.OrderId))
            .Should().BeTrue();
        scenario.PublishedCount<CaptureFundsCommand>().Should().Be(0);
    }

    [Fact]
    public async Task FundsHeld_WhenArrivesLateAfterCustomerAlreadyCancelled_ShouldImmediatelyCompensateByPublishingReleaseFunds()
    {
        // Arrange: Order is submitted and immediately cancelled by the customer before Billing responds.
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        await scenario.SubmitAsync();
        await scenario.PublishAsync(new OrderCancelledByCustomerIntegrationEvent(scenario.OrderId, "Accidental click"));

        // Act: Billing finishes holding the funds a few milliseconds after the saga already entered Cancelled state.
        await scenario.HoldFundsAsync();

        // Assert: Saga stays in Cancelled state and immediately issues a compensating ReleaseFundsHoldCommand.
        OrderSagaState instance = await scenario.ShouldBeInStateAsync(x => x.Cancelled);
        instance.PaymentHoldId.Should().Be(scenario.PaymentHoldId);
        instance.PaymentHoldTimeoutTokenId.Should().BeNull();

        await scenario.ShouldHaveReleasedHoldAsync();
    }

    [Fact]
    public async Task FullHappyPath_WhenDispatchTimerFiresAndOrderIsDelivered_ShouldCaptureFundsAndTransitionToCompleted()
    {
        // Arrange
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);

        // Act: Step through the entire lifecycle.
        await scenario.ReachMatchingCourierAsync();
        await scenario.PublishAsync(new OrderPickedUpIntegrationEvent(scenario.OrderId, scenario.CourierId));
        await scenario.PublishAsync(new OrderDeliveredIntegrationEvent(scenario.OrderId, scenario.CourierId));

        // Assert
        OrderSagaState instance = await scenario.ShouldBeInStateAsync(x => x.Completed);
        instance.AssignedCourierId.Should().Be(scenario.CourierId);
        instance.CourierPickupTimeoutTokenId.Should().BeNull();

        (await scenario.WasPublishedAsync<OrderDeliveryStartedIntegrationEvent>(
            x => x.OrderId == scenario.OrderId && x.CourierId == scenario.CourierId))
            .Should().BeTrue();
        (await scenario.WasPublishedAsync<CaptureFundsCommand>(
            x => x.OrderId == scenario.OrderId && x.PaymentHoldId == scenario.PaymentHoldId))
            .Should().BeTrue();
        (await scenario.WasPublishedAsync<OrderFulfillmentCompletedIntegrationEvent>(x => x.OrderId == scenario.OrderId))
            .Should().BeTrue();
    }

    [Fact]
    public async Task CompletedSaga_WhenEarlierMessagesAreRedelivered_ShouldIgnoreThemWithoutFaultingOrCapturingTwice()
    {
        // Arrange
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        await scenario.ReachMatchingCourierAsync();
        await scenario.PublishAsync(new OrderPickedUpIntegrationEvent(scenario.OrderId, scenario.CourierId));
        await scenario.PublishAsync(new OrderDeliveredIntegrationEvent(scenario.OrderId, scenario.CourierId));

        // Act
        await scenario.PublishAsync(new OrderDeliveredIntegrationEvent(scenario.OrderId, scenario.CourierId));
        await scenario.PublishAsync(new OrderPickedUpIntegrationEvent(scenario.OrderId, scenario.CourierId));
        await scenario.HoldFundsAsync();
        await scenario.AcceptByKitchenAsync();
        await scenario.TriggerCourierDispatchAsync();
        await scenario.PublishAsync(new OrderCancelledByCustomerIntegrationEvent(scenario.OrderId, "Too late"));

        // Assert
        await scenario.ShouldBeInStateAsync(x => x.Completed);
        scenario.ShouldHaveNoFaults();
        scenario.PublishedCount<CaptureFundsCommand>().Should().Be(1);
        scenario.PublishedCount<ReleaseFundsHoldCommand>().Should().Be(0);
    }

    [Fact]
    public async Task CancelledSaga_WhenEarlierMessagesAreRedelivered_ShouldIgnoreThemWithoutFaulting()
    {
        // Arrange
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        await scenario.ReachMatchingCourierAsync();
        await scenario.PublishAsync(new CourierMatchingFailedIntegrationEvent(scenario.OrderId, "No couriers nearby"));

        // Act
        await scenario.PublishAsync(new CourierMatchingFailedIntegrationEvent(scenario.OrderId, "No couriers nearby"));
        await scenario.PublishAsync(new KitchenRejectedOrderIntegrationEvent(scenario.OrderId, "Dish burned"));
        await scenario.PublishAsync(new FundsHoldFailedIntegrationEvent(scenario.OrderId, "Card declined"));
        await scenario.PublishAsync(new OrderCancelledByCustomerIntegrationEvent(scenario.OrderId, "Changed my mind"));
        await scenario.TriggerCourierDispatchAsync();
        await scenario.PublishAsync(new OrderPickedUpIntegrationEvent(scenario.OrderId, scenario.CourierId));
        await scenario.PublishAsync(new OrderDeliveredIntegrationEvent(scenario.OrderId, scenario.CourierId));

        // Assert
        OrderSagaState instance = await scenario.ShouldBeInStateAsync(x => x.Cancelled);
        instance.FailureReason.Should().Be("No couriers nearby");
        scenario.ShouldHaveNoFaults();
        scenario.PublishedCount<OrderFulfillmentFailedIntegrationEvent>().Should().Be(1);
        scenario.PublishedCount<CaptureFundsCommand>().Should().Be(0);
    }

    public enum UnscheduledTimeout
    {
        PaymentHold,
        KitchenConfirmation,
        CourierDispatch,
        CourierPickup
    }

    [Fact]
    public async Task ScheduledTimeout_WhenDeliveredByBrokerWhileStillCurrent_ShouldFire()
    {
        // Arrange: control case proving that broker redelivery keeps the token, so the stale-timeout tests below
        // are ignored because of the token mismatch and not because the header got lost.
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        await scenario.SubmitAsync();
        ISentMessage<PaymentHoldTimeoutExpired> paymentTimeout = scenario.CaptureScheduled<PaymentHoldTimeoutExpired>();

        // Act
        await scenario.DeliverFromBrokerAsync(paymentTimeout);

        // Assert
        OrderSagaState instance = await scenario.ShouldBeInStateAsync(x => x.Cancelled);
        instance.FailureReason.Should().Be(OrderFulfillmentStateMachine.PaymentHoldTimedOutReason);
        instance.PaymentHoldTimeoutTokenId.Should().BeNull();
    }

    [Fact]
    public async Task PaymentHoldTimeout_WhenUnscheduledByFundsHeld_ShouldBeIgnoredInEveryLaterState()
    {
        // Arrange
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        await scenario.SubmitAsync();
        ISentMessage<PaymentHoldTimeoutExpired> staleTimeout = scenario.CaptureScheduled<PaymentHoldTimeoutExpired>();

        // Act & Assert: the delayed exchange may deliver the cancelled timeout at any point of the remaining lifecycle.
        await scenario.HoldFundsAsync();
        await scenario.DeliverFromBrokerAsync(staleTimeout);
        await scenario.ShouldBeInStateAsync(x => x.AwaitingKitchenConfirmation);

        await scenario.AcceptByKitchenAsync();
        await scenario.DeliverFromBrokerAsync(staleTimeout);
        await scenario.ShouldBeInStateAsync(x => x.CookingWaitingForDispatchWindow);

        await scenario.TriggerCourierDispatchAsync();
        await scenario.DeliverFromBrokerAsync(staleTimeout);
        await scenario.ShouldBeInStateAsync(x => x.MatchingCourier);

        await scenario.PublishAsync(new OrderPickedUpIntegrationEvent(scenario.OrderId, scenario.CourierId));
        await scenario.DeliverFromBrokerAsync(staleTimeout);
        await scenario.ShouldBeInStateAsync(x => x.Delivering);

        await scenario.PublishAsync(new OrderDeliveredIntegrationEvent(scenario.OrderId, scenario.CourierId));
        await scenario.DeliverFromBrokerAsync(staleTimeout);
        await scenario.ShouldBeInStateAsync(x => x.Completed);

        scenario.ShouldHaveNoFaults();
        scenario.PublishedCount<OrderFulfillmentFailedIntegrationEvent>().Should().Be(0);
    }

    [Fact]
    public async Task KitchenConfirmationTimeout_WhenUnscheduledByKitchenAccepted_ShouldBeIgnoredInEveryLaterState()
    {
        // Arrange
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        await scenario.SubmitAsync();
        await scenario.HoldFundsAsync();
        ISentMessage<KitchenConfirmationTimeoutExpired> staleTimeout =
            scenario.CaptureScheduled<KitchenConfirmationTimeoutExpired>();

        // Act & Assert
        await scenario.AcceptByKitchenAsync();
        await scenario.DeliverFromBrokerAsync(staleTimeout);
        await scenario.ShouldBeInStateAsync(x => x.CookingWaitingForDispatchWindow);

        await scenario.TriggerCourierDispatchAsync();
        await scenario.DeliverFromBrokerAsync(staleTimeout);
        await scenario.ShouldBeInStateAsync(x => x.MatchingCourier);

        await scenario.PublishAsync(new OrderPickedUpIntegrationEvent(scenario.OrderId, scenario.CourierId));
        await scenario.DeliverFromBrokerAsync(staleTimeout);
        await scenario.ShouldBeInStateAsync(x => x.Delivering);

        await scenario.PublishAsync(new OrderDeliveredIntegrationEvent(scenario.OrderId, scenario.CourierId));
        await scenario.DeliverFromBrokerAsync(staleTimeout);
        await scenario.ShouldBeInStateAsync(x => x.Completed);

        scenario.ShouldHaveNoFaults();
        scenario.PublishedCount<ReleaseFundsHoldCommand>().Should().Be(0);
        scenario.PublishedCount<OrderFulfillmentFailedIntegrationEvent>().Should().Be(0);
    }

    [Fact]
    public async Task CourierDispatchTrigger_WhenBrokerRedeliversItAfterItAlreadyFired_ShouldNotStartMatchingTwice()
    {
        // Arrange
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        await scenario.SubmitAsync();
        await scenario.HoldFundsAsync();
        await scenario.AcceptByKitchenAsync();
        ISentMessage<TriggerCourierDispatchTimeout> dispatchTrigger =
            scenario.CaptureScheduled<TriggerCourierDispatchTimeout>();

        await scenario.DeliverFromBrokerAsync(dispatchTrigger);
        await scenario.ShouldBeInStateAsync(x => x.MatchingCourier);
        scenario.CurrentTokenId(x => x.CourierDispatchScheduleTokenId).Should().BeNull();

        // Act & Assert: at-least-once delivery may hand the same scheduled message over again in any later state.
        await scenario.DeliverFromBrokerAsync(dispatchTrigger);
        await scenario.ShouldBeInStateAsync(x => x.MatchingCourier);

        await scenario.PublishAsync(new OrderPickedUpIntegrationEvent(scenario.OrderId, scenario.CourierId));
        await scenario.DeliverFromBrokerAsync(dispatchTrigger);
        await scenario.ShouldBeInStateAsync(x => x.Delivering);

        await scenario.PublishAsync(new OrderDeliveredIntegrationEvent(scenario.OrderId, scenario.CourierId));
        await scenario.DeliverFromBrokerAsync(dispatchTrigger);
        await scenario.ShouldBeInStateAsync(x => x.Completed);

        scenario.ShouldHaveNoFaults();
        scenario.PublishedCount<StartCourierMatchingCommand>().Should().Be(1);
    }

    [Fact]
    public async Task CourierPickupTimeout_WhenUnscheduledByOrderPickedUp_ShouldBeIgnoredInEveryLaterState()
    {
        // Arrange
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        await scenario.ReachMatchingCourierAsync();
        ISentMessage<CourierPickupTimeoutExpired> staleTimeout = scenario.CaptureScheduled<CourierPickupTimeoutExpired>();

        // Act & Assert
        await scenario.PublishAsync(new OrderPickedUpIntegrationEvent(scenario.OrderId, scenario.CourierId));
        await scenario.DeliverFromBrokerAsync(staleTimeout);
        await scenario.ShouldBeInStateAsync(x => x.Delivering);

        await scenario.PublishAsync(new OrderDeliveredIntegrationEvent(scenario.OrderId, scenario.CourierId));
        await scenario.DeliverFromBrokerAsync(staleTimeout);
        await scenario.ShouldBeInStateAsync(x => x.Completed);

        scenario.ShouldHaveNoFaults();
        scenario.PublishedCount<CancelDeliveryCommand>().Should().Be(0);
        scenario.PublishedCount<ReleaseFundsHoldCommand>().Should().Be(0);
        scenario.PublishedCount<CaptureFundsCommand>().Should().Be(1);
    }

    [Theory]
    [InlineData(UnscheduledTimeout.PaymentHold)]
    [InlineData(UnscheduledTimeout.KitchenConfirmation)]
    [InlineData(UnscheduledTimeout.CourierDispatch)]
    [InlineData(UnscheduledTimeout.CourierPickup)]
    public async Task UnscheduledTimeout_WhenDeliveredAfterSagaWasCancelled_ShouldBeIgnoredWithoutCompensatingTwice(
        UnscheduledTimeout timeout)
    {
        // Arrange: each timeout is armed and then unscheduled by the failure that cancels the saga.
        await using SagaScenario scenario = await SagaScenario.StartAsync(TestContext.Current.CancellationToken);
        Func<Task> deliverStaleTimeout;

        switch (timeout)
        {
            case UnscheduledTimeout.PaymentHold:
                {
                    await scenario.SubmitAsync();
                    ISentMessage<PaymentHoldTimeoutExpired> stale = scenario.CaptureScheduled<PaymentHoldTimeoutExpired>();
                    await scenario.PublishAsync(new OrderCancelledByCustomerIntegrationEvent(scenario.OrderId, "Changed my mind"));
                    deliverStaleTimeout = () => scenario.DeliverFromBrokerAsync(stale);
                    break;
                }

            case UnscheduledTimeout.KitchenConfirmation:
                {
                    await scenario.SubmitAsync();
                    await scenario.HoldFundsAsync();
                    ISentMessage<KitchenConfirmationTimeoutExpired> stale =
                        scenario.CaptureScheduled<KitchenConfirmationTimeoutExpired>();
                    await scenario.PublishAsync(new KitchenRejectedOrderIntegrationEvent(scenario.OrderId, "Salmon out of stock"));
                    deliverStaleTimeout = () => scenario.DeliverFromBrokerAsync(stale);
                    break;
                }

            case UnscheduledTimeout.CourierDispatch:
                {
                    await scenario.SubmitAsync();
                    await scenario.HoldFundsAsync();
                    await scenario.AcceptByKitchenAsync();
                    ISentMessage<TriggerCourierDispatchTimeout> stale =
                        scenario.CaptureScheduled<TriggerCourierDispatchTimeout>();
                    await scenario.PublishAsync(new KitchenRejectedOrderIntegrationEvent(scenario.OrderId, "Oven broke down"));
                    deliverStaleTimeout = () => scenario.DeliverFromBrokerAsync(stale);
                    break;
                }

            case UnscheduledTimeout.CourierPickup:
                {
                    await scenario.ReachMatchingCourierAsync();
                    ISentMessage<CourierPickupTimeoutExpired> stale = scenario.CaptureScheduled<CourierPickupTimeoutExpired>();
                    await scenario.PublishAsync(new CourierMatchingFailedIntegrationEvent(scenario.OrderId, "No couriers nearby"));
                    deliverStaleTimeout = () => scenario.DeliverFromBrokerAsync(stale);
                    break;
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(timeout), timeout, null);
        }

        OrderSagaState cancelled = await scenario.ShouldBeInStateAsync(x => x.Cancelled);
        string? failureReason = cancelled.FailureReason;
        int failedEventsBefore = scenario.PublishedCount<OrderFulfillmentFailedIntegrationEvent>();
        int releasesBefore = scenario.PublishedCount<ReleaseFundsHoldCommand>();

        // Act
        await deliverStaleTimeout();

        // Assert
        OrderSagaState instance = await scenario.ShouldBeInStateAsync(x => x.Cancelled);
        instance.FailureReason.Should().Be(failureReason);
        scenario.ShouldHaveNoFaults();
        scenario.PublishedCount<OrderFulfillmentFailedIntegrationEvent>().Should().Be(failedEventsBefore);
        scenario.PublishedCount<ReleaseFundsHoldCommand>().Should().Be(releasesBefore);
        scenario.PublishedCount<StartCourierMatchingCommand>().Should().Be(timeout == UnscheduledTimeout.CourierPickup ? 1 : 0);
        scenario.PublishedCount<CancelDeliveryCommand>().Should().Be(0);
    }

    private sealed class SagaScenario : IAsyncDisposable
    {
        private static readonly TimeSpan ConsumptionTimeout = TimeSpan.FromSeconds(10);

        private readonly ServiceProvider _provider;
        private readonly ITestHarness _harness;
        private readonly ISagaStateMachineTestHarness<OrderFulfillmentStateMachine, OrderSagaState> _sagaHarness;
        private readonly CancellationToken _cancellationToken;

        private SagaScenario(ServiceProvider provider, ITestHarness harness, CancellationToken cancellationToken)
        {
            _provider = provider;
            _harness = harness;
            _sagaHarness = harness.GetSagaStateMachineHarness<OrderFulfillmentStateMachine, OrderSagaState>();
            _cancellationToken = cancellationToken;
        }

        public Guid OrderId { get; } = Guid.NewGuid();

        public Guid PaymentHoldId { get; } = Guid.NewGuid();

        public Guid CourierId { get; } = Guid.NewGuid();

        public static async Task<SagaScenario> StartAsync(CancellationToken cancellationToken)
        {
            ServiceCollection services = new();

            services.AddMassTransitTestHarness(cfg =>
            {
                cfg.AddDelayedMessageScheduler();
                cfg.AddSagaStateMachine<OrderFulfillmentStateMachine, OrderSagaState>();

                cfg.UsingInMemory((context, busCfg) =>
                {
                    busCfg.UseDelayedMessageScheduler();
                    busCfg.ConfigureEndpoints(context);
                });
            });

            ServiceProvider provider = services.BuildServiceProvider(true);
            ITestHarness harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();

            // Every step is synchronised explicitly by DeliverAndWaitAsync, so the harness never has to detect bus
            // idleness. Without this the first Select on a harness list blocks for the whole inactivity timeout
            // (~1.2 s), which is paid again by every test because each one builds its own harness.
            harness.ForceInactive();

            return new SagaScenario(provider, harness, cancellationToken);
        }

        public Task SubmitAsync() =>
            PublishAsync(new OrderSubmittedIntegrationEvent(OrderId, Guid.NewGuid(), Guid.NewGuid(), TotalAmount, Currency));

        public Task HoldFundsAsync() => PublishAsync(new FundsHeldIntegrationEvent(OrderId, PaymentHoldId));

        public Task AcceptByKitchenAsync() =>
            PublishAsync(new OrderAcceptedByKitchenIntegrationEvent(OrderId, EstimatedPrepMinutes));

        public Task TriggerCourierDispatchAsync() => PublishAsync(new TriggerCourierDispatchTimeout(OrderId));

        public async Task ReachMatchingCourierAsync()
        {
            await SubmitAsync();
            await HoldFundsAsync();
            await AcceptByKitchenAsync();
            await TriggerCourierDispatchAsync();
            await ShouldBeInStateAsync(x => x.MatchingCourier);
        }

        // Waits until the saga has finished consuming the message, so every step observes the previous transition
        // and redeliveries are processed in a deterministic order.
        public Task PublishAsync<T>(T message)
            where T : class =>
            DeliverAndWaitAsync<T>(() => _harness.Bus.Publish(message, _cancellationToken));

        // Returns the timeout exactly as the saga handed it to the scheduler, including the scheduling token header
        // that MassTransit compares against the token stored on the saga instance.
        public ISentMessage<T> CaptureScheduled<T>()
            where T : class
        {
            ISentMessage<T> scheduled = _harness.Sent.Select<T>(_cancellationToken).Last();
            scheduled.Context.Headers.Get<Guid>(MessageHeaders.SchedulingTokenId).Should().NotBeNull();

            return scheduled;
        }

        // Simulates the RabbitMQ delayed exchange firing: Unschedule cannot remove the message from the broker, so it
        // reaches the saga queue unchanged, with its original headers, long after the saga has moved on.
        public async Task DeliverFromBrokerAsync<T>(ISentMessage<T> scheduled)
            where T : class
        {
            ISendEndpoint sagaEndpoint = await _harness.GetSagaEndpoint<OrderSagaState>();

            await DeliverAndWaitAsync<T>(() => sagaEndpoint.Send(
                scheduled.Context.Message,
                ctx =>
                {
                    foreach (KeyValuePair<string, object> header in scheduled.Context.Headers.GetAll())
                    {
                        ctx.Headers.Set(header.Key, header.Value);
                    }
                },
                _cancellationToken));
        }

        public Guid? CurrentTokenId(Func<OrderSagaState, Guid?> tokenSelector) =>
            tokenSelector(_sagaHarness.Sagas.Contains(OrderId));

        private async Task DeliverAndWaitAsync<T>(Func<Task> deliver)
            where T : class
        {
            int consumedBefore = _sagaHarness.Consumed.Select<T>(_cancellationToken).Count();

            await deliver();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
            timeout.CancelAfter(ConsumptionTimeout);

            while (_sagaHarness.Consumed.Select<T>(_cancellationToken).Count() <= consumedBefore)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
            }
        }

        public async Task<OrderSagaState> ShouldBeInStateAsync(Func<OrderFulfillmentStateMachine, State> stateSelector)
        {
            Guid? correlationId = await _sagaHarness.Exists(OrderId, stateSelector);
            correlationId.Should().Be(OrderId, "saga should be in state {0}", stateSelector(_sagaHarness.StateMachine).Name);

            return _sagaHarness.Sagas.Contains(OrderId);
        }

        public void ShouldHaveNoFaults() =>
            _sagaHarness.Consumed.Select(x => x.Exception is not null, _cancellationToken).Should().BeEmpty();

        public Task<bool> WasPublishedAsync<T>(Func<T, bool> predicate)
            where T : class =>
            _harness.Published.Any<T>(x => predicate(x.Context.Message), _cancellationToken);

        public int PublishedCount<T>()
            where T : class =>
            _harness.Published.Select<T>(_cancellationToken).Count();

        public async Task ShouldHaveReleasedHoldAsync() =>
            (await WasPublishedAsync<ReleaseFundsHoldCommand>(x => x.OrderId == OrderId && x.PaymentHoldId == PaymentHoldId))
                .Should().BeTrue();

        public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
    }
}
