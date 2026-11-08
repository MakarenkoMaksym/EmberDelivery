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
    [Fact]
    public async Task OrderSubmitted_ShouldInitializeSagaInAwaitingPaymentHoldAndPublishHoldFundsCommand()
    {
        // Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider provider = CreateHarnessProvider();
        ITestHarness harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        // Act
        await harness.Bus.Publish(
            new OrderSubmittedIntegrationEvent(
                orderId,
                customerId,
                restaurantId,
                TotalAmount: 450m,
                Currency: "UAH"),
            cancellationToken);

        // Assert
        ISagaStateMachineTestHarness<OrderFulfillmentStateMachine, OrderSagaState> sagaHarness =
            harness.GetSagaStateMachineHarness<OrderFulfillmentStateMachine, OrderSagaState>();

        (await sagaHarness.Consumed.Any<OrderSubmittedIntegrationEvent>(cancellationToken)).Should().BeTrue();
        (await sagaHarness.Created.Any(x => x.CorrelationId == orderId, cancellationToken)).Should().BeTrue();

        OrderSagaState instance = sagaHarness.Created.ContainsInState(
            orderId,
            sagaHarness.StateMachine,
            sagaHarness.StateMachine.AwaitingPaymentHold);

        instance.Should().NotBeNull();
        instance.TotalAmount.Should().Be(450m);

        (await harness.Published.Any<HoldFundsCommand>(
            x => x.Context.Message.OrderId == orderId && x.Context.Message.Amount == 450m,
            cancellationToken))
            .Should().BeTrue();
    }

    [Fact]
    public async Task KitchenRejected_WhenPaymentWasAlreadyHeld_ShouldPublishReleaseFundsAndTransitionToCancelled()
    {
        // Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider provider = CreateHarnessProvider();
        ITestHarness harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var orderId = Guid.NewGuid();
        var paymentHoldId = Guid.NewGuid();

        await harness.Bus.Publish(
            new OrderSubmittedIntegrationEvent(
                orderId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                TotalAmount: 380m,
                Currency: "UAH"),
            cancellationToken);

        await harness.Bus.Publish(new FundsHeldIntegrationEvent(orderId, paymentHoldId), cancellationToken);

        // Act: Kitchen rejects the order because salmon is out of stock.
        await harness.Bus.Publish(
            new KitchenRejectedOrderIntegrationEvent(orderId, "Salmon out of stock"),
            cancellationToken);

        // Assert
        ISagaStateMachineTestHarness<OrderFulfillmentStateMachine, OrderSagaState> sagaHarness =
            harness.GetSagaStateMachineHarness<OrderFulfillmentStateMachine, OrderSagaState>();

        (await sagaHarness.Consumed.Any<KitchenRejectedOrderIntegrationEvent>(cancellationToken)).Should().BeTrue();

        OrderSagaState instance = sagaHarness.Created.ContainsInState(
            orderId,
            sagaHarness.StateMachine,
            sagaHarness.StateMachine.Cancelled);

        instance.Should().NotBeNull();
        instance.FailureReason.Should().Be("Salmon out of stock");

        // Verify compensation 1: Billing was commanded to release the held money.
        (await harness.Published.Any<ReleaseFundsHoldCommand>(
            x => x.Context.Message.OrderId == orderId && x.Context.Message.PaymentHoldId == paymentHoldId,
            cancellationToken))
            .Should().BeTrue();

        // Verify compensation 2: Ordering and Realtime UI were notified of the failure.
        (await harness.Published.Any<OrderFulfillmentFailedIntegrationEvent>(
            x => x.Context.Message.OrderId == orderId,
            cancellationToken))
            .Should().BeTrue();
    }

    [Fact]
    public async Task OrderCancelledByCustomer_WhenInAwaitingKitchenConfirmation_ShouldPublishReleaseFundsAndTransitionToCancelled()
    {
        // Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider provider = CreateHarnessProvider();
        ITestHarness harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var orderId = Guid.NewGuid();
        var paymentHoldId = Guid.NewGuid();

        await harness.Bus.Publish(
            new OrderSubmittedIntegrationEvent(
                orderId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                TotalAmount: 420m,
                Currency: "UAH"),
            cancellationToken);

        await harness.Bus.Publish(new FundsHeldIntegrationEvent(orderId, paymentHoldId), cancellationToken);

        // Act: Customer cancels the order while it is Paid and awaiting kitchen confirmation.
        await harness.Bus.Publish(
            new OrderCancelledByCustomerIntegrationEvent(orderId, "Changed my mind"),
            cancellationToken);

        // Assert
        ISagaStateMachineTestHarness<OrderFulfillmentStateMachine, OrderSagaState> sagaHarness =
            harness.GetSagaStateMachineHarness<OrderFulfillmentStateMachine, OrderSagaState>();

        (await sagaHarness.Consumed.Any<OrderCancelledByCustomerIntegrationEvent>(cancellationToken)).Should().BeTrue();

        OrderSagaState instance = sagaHarness.Created.ContainsInState(
            orderId,
            sagaHarness.StateMachine,
            sagaHarness.StateMachine.Cancelled);

        instance.Should().NotBeNull();
        instance.FailureReason.Should().Be("Changed my mind");

        (await harness.Published.Any<ReleaseFundsHoldCommand>(
            x => x.Context.Message.OrderId == orderId && x.Context.Message.PaymentHoldId == paymentHoldId,
            cancellationToken))
            .Should().BeTrue();
    }

    [Fact]
    public async Task FundsHeld_WhenArrivesLateAfterCustomerAlreadyCancelled_ShouldImmediatelyCompensateByPublishingReleaseFunds()
    {
        // Arrange: Order is submitted and immediately cancelled by the customer before Billing responds.
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider provider = CreateHarnessProvider();
        ITestHarness harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var orderId = Guid.NewGuid();
        var latePaymentHoldId = Guid.NewGuid();

        await harness.Bus.Publish(
            new OrderSubmittedIntegrationEvent(
                orderId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                TotalAmount: 310m,
                Currency: "UAH"),
            cancellationToken);

        await harness.Bus.Publish(
            new OrderCancelledByCustomerIntegrationEvent(orderId, "Accidental click"),
            cancellationToken);

        ISagaStateMachineTestHarness<OrderFulfillmentStateMachine, OrderSagaState> sagaHarness =
            harness.GetSagaStateMachineHarness<OrderFulfillmentStateMachine, OrderSagaState>();

        (await sagaHarness.Consumed.Any<OrderCancelledByCustomerIntegrationEvent>(cancellationToken)).Should().BeTrue();

        // Act: Billing finishes holding the funds a few milliseconds after the saga already entered Cancelled state.
        await harness.Bus.Publish(new FundsHeldIntegrationEvent(orderId, latePaymentHoldId), cancellationToken);

        // Assert: Saga stays in Cancelled state and immediately issues a compensating ReleaseFundsHoldCommand.
        (await sagaHarness.Consumed.Any<FundsHeldIntegrationEvent>(cancellationToken)).Should().BeTrue();

        OrderSagaState instance = sagaHarness.Created.ContainsInState(
            orderId,
            sagaHarness.StateMachine,
            sagaHarness.StateMachine.Cancelled);

        instance.Should().NotBeNull();
        instance.PaymentHoldId.Should().Be(latePaymentHoldId);

        (await harness.Published.Any<ReleaseFundsHoldCommand>(
            x => x.Context.Message.OrderId == orderId && x.Context.Message.PaymentHoldId == latePaymentHoldId,
            cancellationToken))
            .Should().BeTrue();
    }

    [Fact]
    public async Task FullHappyPath_WhenDispatchTimerFiresAndOrderIsDelivered_ShouldCaptureFundsAndTransitionToCompleted()
    {
        // Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider provider = CreateHarnessProvider();
        ITestHarness harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var orderId = Guid.NewGuid();
        var paymentHoldId = Guid.NewGuid();
        var courierId = Guid.NewGuid();

        // Act: Step through the entire lifecycle.
        await harness.Bus.Publish(
            new OrderSubmittedIntegrationEvent(
                orderId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                TotalAmount: 500m,
                Currency: "UAH"),
            cancellationToken);

        await harness.Bus.Publish(new FundsHeldIntegrationEvent(orderId, paymentHoldId), cancellationToken);
        await harness.Bus.Publish(
            new OrderAcceptedByKitchenIntegrationEvent(orderId, EstimatedPrepMinutes: 20),
            cancellationToken);

        // Simulate the Just-in-Time schedule trigger firing 7 minutes before prep finishes.
        await harness.Bus.Publish(new TriggerCourierDispatchTimeout(orderId), cancellationToken);

        await harness.Bus.Publish(new OrderPickedUpIntegrationEvent(orderId, courierId), cancellationToken);
        await harness.Bus.Publish(new OrderDeliveredIntegrationEvent(orderId, courierId), cancellationToken);

        // Assert
        ISagaStateMachineTestHarness<OrderFulfillmentStateMachine, OrderSagaState> sagaHarness =
            harness.GetSagaStateMachineHarness<OrderFulfillmentStateMachine, OrderSagaState>();

        (await sagaHarness.Consumed.Any<OrderDeliveredIntegrationEvent>(cancellationToken)).Should().BeTrue();

        OrderSagaState instance = sagaHarness.Created.ContainsInState(
            orderId,
            sagaHarness.StateMachine,
            sagaHarness.StateMachine.Completed);

        instance.Should().NotBeNull();
        instance.AssignedCourierId.Should().Be(courierId);

        (await harness.Published.Any<StartCourierMatchingCommand>(
            x => x.Context.Message.OrderId == orderId,
            cancellationToken))
            .Should().BeTrue();

        (await harness.Published.Any<CaptureFundsCommand>(
            x => x.Context.Message.OrderId == orderId && x.Context.Message.PaymentHoldId == paymentHoldId,
            cancellationToken))
            .Should().BeTrue();
    }

    private static ServiceProvider CreateHarnessProvider()
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

        return services.BuildServiceProvider(true);
    }
}
