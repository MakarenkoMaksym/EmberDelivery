using EmberDelivery.Ordering.IntegrationEvents;

using MassTransit;

using static EmberDelivery.Billing.IntegrationEvents.PaymentContracts;
using static EmberDelivery.Dispatch.IntegrationEvents.CourierContracts;
using static EmberDelivery.Kitchen.IntegrationEvents.KitchenSlotContracts;
using static EmberDelivery.Ordering.IntegrationEvents.OrderSagaLifecycleEvents;
using static EmberDelivery.Ordering.IntegrationEvents.OrderSagaTimeouts;

namespace EmberDelivery.Ordering.Application.Sagas.OrderFulfillment;

// Message handling policy: duplicates and legitimately late messages are ignored or compensated in every state,
// while contradictory messages (e.g. FundsHoldFailed after FundsHeld) are deliberately left unhandled so they land
// in the _error queue and surface a bug in the sending service instead of being silently swallowed.
public sealed class OrderFulfillmentStateMachine : MassTransitStateMachine<OrderSagaState>
{
    public const string PaymentHoldTimedOutReason = "Payment provider did not respond in time.";

    public const string KitchenConfirmationTimedOutReason = "Restaurant did not confirm the order in time.";

    public const string CourierPickupTimedOutReason = "No courier picked up the order in time.";

    public static readonly TimeSpan PaymentHoldTimeoutDelay = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan KitchenConfirmationTimeoutDelay = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan CourierPickupTimeoutDelay = TimeSpan.FromMinutes(30);

    private const int DefaultCourierApproachMinutes = 7;

    public OrderFulfillmentStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => OrderSubmitted, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => OrderCancelledByCustomer, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => FundsHeld, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => FundsHoldFailed, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => KitchenAccepted, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => KitchenRejected, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => CourierMatchingFailed, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => OrderPickedUp, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => OrderDelivered, x => x.CorrelateById(ctx => ctx.Message.OrderId));

        Schedule(
            () => CourierDispatchSchedule,
            instance => instance.CourierDispatchScheduleTokenId,
            cfg => cfg.Received = e => e.CorrelateById(ctx => ctx.Message.OrderId));

        // Stale timeouts are filtered by MassTransit through the token id, which matters because the RabbitMQ delayed
        // scheduler cannot actually cancel a message: Unschedule only clears the token so the firing is discarded.
        Schedule(
            () => PaymentHoldTimeout,
            instance => instance.PaymentHoldTimeoutTokenId,
            cfg =>
            {
                cfg.Delay = PaymentHoldTimeoutDelay;
                cfg.Received = e => e.CorrelateById(ctx => ctx.Message.OrderId);
            });

        Schedule(
            () => KitchenConfirmationTimeout,
            instance => instance.KitchenConfirmationTimeoutTokenId,
            cfg =>
            {
                cfg.Delay = KitchenConfirmationTimeoutDelay;
                cfg.Received = e => e.CorrelateById(ctx => ctx.Message.OrderId);
            });

        Schedule(
            () => CourierPickupTimeout,
            instance => instance.CourierPickupTimeoutTokenId,
            cfg =>
            {
                cfg.Delay = CourierPickupTimeoutDelay;
                cfg.Received = e => e.CorrelateById(ctx => ctx.Message.OrderId);
            });

        // Redelivered OrderSubmitted must never fault an existing saga, whatever state it has reached.
        DuringAny(Ignore(OrderSubmitted));

        // Phase 1: Order submitted -> initialize saga and command Billing to hold funds on customer's card.
        Initially(
            When(OrderSubmitted)
                .Then(ctx =>
                {
                    ctx.Saga.CustomerId = ctx.Message.CustomerId;
                    ctx.Saga.RestaurantId = ctx.Message.RestaurantId;
                    ctx.Saga.TotalAmount = ctx.Message.TotalAmount;
                    ctx.Saga.Currency = ctx.Message.Currency;
                })
                .Publish(ctx => new HoldFundsCommand(
                    ctx.Saga.CorrelationId,
                    ctx.Saga.CustomerId,
                    ctx.Saga.TotalAmount,
                    ctx.Saga.Currency))
                .Schedule(PaymentHoldTimeout, ctx => new PaymentHoldTimeoutExpired(ctx.Saga.CorrelationId))
                .TransitionTo(AwaitingPaymentHold));

        // Phase 2: Payment hold result from Billing, timeout, or early customer cancellation.
        // No release is issued on cancellation or timeout here: the hold id is unknown yet, and a hold that Billing
        // completes later is compensated by the late FundsHeld handler in Cancelled.
        During(AwaitingPaymentHold,
            When(FundsHeld)
                .Unschedule(PaymentHoldTimeout)
                .Then(ctx =>
                {
                    ctx.Saga.PaymentHoldId = ctx.Message.PaymentHoldId;
                    ctx.Saga.Version++;
                })
                .Publish(ctx => new OrderPaymentConfirmedIntegrationEvent(ctx.Saga.CorrelationId))
                .Publish(ctx => new ConfirmKitchenCapacityCommand(
                    ctx.Saga.CorrelationId,
                    ctx.Saga.RestaurantId))
                .Schedule(KitchenConfirmationTimeout, ctx => new KitchenConfirmationTimeoutExpired(ctx.Saga.CorrelationId))
                .TransitionTo(AwaitingKitchenConfirmation),

            When(FundsHoldFailed)
                .Unschedule(PaymentHoldTimeout)
                .Then(ctx => MarkCancelled(ctx.Saga, ctx.Message.Reason))
                .Publish(ctx => new OrderFulfillmentFailedIntegrationEvent(
                    ctx.Saga.CorrelationId,
                    ctx.Message.Reason))
                .TransitionTo(Cancelled),

            When(PaymentHoldTimeout.Received)
                .Then(ctx => MarkCancelled(ctx.Saga, PaymentHoldTimedOutReason))
                .Publish(ctx => new OrderFulfillmentFailedIntegrationEvent(
                    ctx.Saga.CorrelationId,
                    PaymentHoldTimedOutReason))
                .TransitionTo(Cancelled),

            When(OrderCancelledByCustomer)
                .Unschedule(PaymentHoldTimeout)
                .Then(ctx => MarkCancelled(ctx.Saga, ctx.Message.Reason))
                .TransitionTo(Cancelled));

        // Phase 3: Kitchen capacity confirmation, kitchen rejection, timeout, or customer cancellation while Paid.
        // Kitchen is not told to cancel here because it has not accepted yet; a late acceptance is compensated in
        // Cancelled, which keeps Kitchen free of "cancel before confirm" tombstone logic.
        During(AwaitingKitchenConfirmation,
            Ignore(FundsHeld),

            When(KitchenAccepted)
                .Unschedule(KitchenConfirmationTimeout)
                .Then(ctx => ctx.Saga.Version++)
                .Publish(ctx => new OrderCookingStartedIntegrationEvent(
                    ctx.Saga.CorrelationId,
                    ctx.Message.EstimatedPrepMinutes))
                .Schedule(
                    CourierDispatchSchedule,
                    ctx => new TriggerCourierDispatchTimeout(ctx.Saga.CorrelationId),
                    ctx =>
                    {
                        int delayMinutes = Math.Max(0, ctx.Message.EstimatedPrepMinutes - DefaultCourierApproachMinutes);
                        return TimeSpan.FromMinutes(delayMinutes);
                    })
                .TransitionTo(CookingWaitingForDispatchWindow),

            When(KitchenRejected)
                .Unschedule(KitchenConfirmationTimeout)
                .Then(ctx => MarkCancelled(ctx.Saga, ctx.Message.Reason))
                .Publish(ctx => ReleaseHold(ctx.Saga))
                .Publish(ctx => new OrderFulfillmentFailedIntegrationEvent(
                    ctx.Saga.CorrelationId,
                    ctx.Message.Reason))
                .TransitionTo(Cancelled),

            When(KitchenConfirmationTimeout.Received)
                .Then(ctx => MarkCancelled(ctx.Saga, KitchenConfirmationTimedOutReason))
                .Publish(ctx => ReleaseHold(ctx.Saga))
                .Publish(ctx => new OrderFulfillmentFailedIntegrationEvent(
                    ctx.Saga.CorrelationId,
                    KitchenConfirmationTimedOutReason))
                .TransitionTo(Cancelled),

            When(OrderCancelledByCustomer)
                .Unschedule(KitchenConfirmationTimeout)
                .Then(ctx => MarkCancelled(ctx.Saga, ctx.Message.Reason))
                .Publish(ctx => ReleaseHold(ctx.Saga))
                .TransitionTo(Cancelled));

        // Phase 4: Cooking in progress; waiting for Just-in-Time courier dispatch window.
        // The Order aggregate accepts a customer cancellation only while it is Submitted or Paid, so a cancellation
        // arriving from here on was accepted before the saga's progress reached the aggregate. The aggregate has
        // already told the customer "cancelled" and will refuse MarkAsCooking/Delivering/Completed, so the saga must
        // honour it; otherwise funds would be captured for an order the customer sees as cancelled.
        During(CookingWaitingForDispatchWindow,
            Ignore(FundsHeld),
            Ignore(KitchenAccepted),

            When(CourierDispatchSchedule.Received)
                .Then(ctx => ctx.Saga.Version++)
                .Publish(ctx => new StartCourierMatchingCommand(
                    ctx.Saga.CorrelationId,
                    ctx.Saga.RestaurantId,
                    ctx.Saga.CustomerId))
                .Schedule(CourierPickupTimeout, ctx => new CourierPickupTimeoutExpired(ctx.Saga.CorrelationId))
                .TransitionTo(MatchingCourier),

            When(KitchenRejected)
                .Unschedule(CourierDispatchSchedule)
                .Then(ctx => MarkCancelled(ctx.Saga, ctx.Message.Reason))
                .Publish(ctx => ReleaseHold(ctx.Saga))
                .Publish(ctx => new OrderFulfillmentFailedIntegrationEvent(
                    ctx.Saga.CorrelationId,
                    ctx.Message.Reason))
                .TransitionTo(Cancelled),

            When(OrderCancelledByCustomer)
                .Unschedule(CourierDispatchSchedule)
                .Then(ctx => MarkCancelled(ctx.Saga, ctx.Message.Reason))
                .Publish(ctx => new CancelKitchenOrderCommand(ctx.Saga.CorrelationId, ctx.Message.Reason))
                .Publish(ctx => ReleaseHold(ctx.Saga))
                .TransitionTo(Cancelled));

        // Phase 5: Searching for a courier; the food is (almost) ready, so every failure also stops the kitchen.
        During(MatchingCourier,
            Ignore(FundsHeld),
            Ignore(KitchenAccepted),
            Ignore(CourierDispatchSchedule.Received),

            When(OrderPickedUp)
                .Unschedule(CourierPickupTimeout)
                .Then(ctx =>
                {
                    ctx.Saga.AssignedCourierId = ctx.Message.CourierId;
                    ctx.Saga.Version++;
                })
                .Publish(ctx => new OrderDeliveryStartedIntegrationEvent(
                    ctx.Saga.CorrelationId,
                    ctx.Message.CourierId))
                .TransitionTo(Delivering),

            When(CourierMatchingFailed)
                .Unschedule(CourierPickupTimeout)
                .Then(ctx => MarkCancelled(ctx.Saga, ctx.Message.Reason))
                .Publish(ctx => new CancelKitchenOrderCommand(ctx.Saga.CorrelationId, ctx.Message.Reason))
                .Publish(ctx => ReleaseHold(ctx.Saga))
                .Publish(ctx => new OrderFulfillmentFailedIntegrationEvent(
                    ctx.Saga.CorrelationId,
                    ctx.Message.Reason))
                .TransitionTo(Cancelled),

            When(CourierPickupTimeout.Received)
                .Then(ctx => MarkCancelled(ctx.Saga, CourierPickupTimedOutReason))
                .Publish(ctx => new CancelDeliveryCommand(ctx.Saga.CorrelationId, CourierPickupTimedOutReason))
                .Publish(ctx => new CancelKitchenOrderCommand(ctx.Saga.CorrelationId, CourierPickupTimedOutReason))
                .Publish(ctx => ReleaseHold(ctx.Saga))
                .Publish(ctx => new OrderFulfillmentFailedIntegrationEvent(
                    ctx.Saga.CorrelationId,
                    CourierPickupTimedOutReason))
                .TransitionTo(Cancelled),

            When(KitchenRejected)
                .Unschedule(CourierPickupTimeout)
                .Then(ctx => MarkCancelled(ctx.Saga, ctx.Message.Reason))
                .Publish(ctx => new CancelDeliveryCommand(ctx.Saga.CorrelationId, ctx.Message.Reason))
                .Publish(ctx => ReleaseHold(ctx.Saga))
                .Publish(ctx => new OrderFulfillmentFailedIntegrationEvent(
                    ctx.Saga.CorrelationId,
                    ctx.Message.Reason))
                .TransitionTo(Cancelled),

            When(OrderCancelledByCustomer)
                .Unschedule(CourierPickupTimeout)
                .Then(ctx => MarkCancelled(ctx.Saga, ctx.Message.Reason))
                .Publish(ctx => new CancelDeliveryCommand(ctx.Saga.CorrelationId, ctx.Message.Reason))
                .Publish(ctx => new CancelKitchenOrderCommand(ctx.Saga.CorrelationId, ctx.Message.Reason))
                .Publish(ctx => ReleaseHold(ctx.Saga))
                .TransitionTo(Cancelled));

        // Phase 6: Courier hands order to customer -> capture funds and complete.
        // Kitchen-side and matching-side messages that arrive after pickup are outdated and cannot change the outcome.
        During(Delivering,
            Ignore(FundsHeld),
            Ignore(KitchenAccepted),
            Ignore(KitchenRejected),
            Ignore(CourierDispatchSchedule.Received),
            Ignore(CourierMatchingFailed),
            Ignore(OrderPickedUp),

            When(OrderDelivered)
                .Then(ctx => ctx.Saga.Version++)
                .Publish(ctx => new CaptureFundsCommand(
                    ctx.Saga.CorrelationId,
                    ctx.Saga.PaymentHoldId!.Value))
                .Publish(ctx => new OrderFulfillmentCompletedIntegrationEvent(
                    ctx.Saga.CorrelationId,
                    ctx.Message.CourierId))
                .TransitionTo(Completed),

            When(OrderCancelledByCustomer)
                .Then(ctx => MarkCancelled(ctx.Saga, ctx.Message.Reason))
                .Publish(ctx => new CancelDeliveryCommand(ctx.Saga.CorrelationId, ctx.Message.Reason))
                .Publish(ctx => ReleaseHold(ctx.Saga))
                .TransitionTo(Cancelled));

        // Funds are already captured, so only redeliveries and outdated messages can arrive here.
        During(Completed,
            Ignore(OrderCancelledByCustomer),
            Ignore(FundsHeld),
            Ignore(KitchenAccepted),
            Ignore(KitchenRejected),
            Ignore(CourierDispatchSchedule.Received),
            Ignore(CourierMatchingFailed),
            Ignore(OrderPickedUp),
            Ignore(OrderDelivered));

        // Late answers to commands issued before cancellation are compensated so that neither the customer's money
        // nor the restaurant's work is stranded; everything else is a harmless redelivery.
        During(Cancelled,
            Ignore(OrderCancelledByCustomer),
            Ignore(FundsHoldFailed),
            Ignore(KitchenRejected),
            Ignore(CourierDispatchSchedule.Received),
            Ignore(CourierMatchingFailed),
            Ignore(OrderPickedUp),
            Ignore(OrderDelivered),

            When(FundsHeld)
                .Then(ctx =>
                {
                    ctx.Saga.PaymentHoldId = ctx.Message.PaymentHoldId;
                    ctx.Saga.Version++;
                })
                .Publish(ctx => new ReleaseFundsHoldCommand(
                    ctx.Saga.CorrelationId,
                    ctx.Message.PaymentHoldId,
                    ctx.Saga.FailureReason ?? "Order was cancelled before payment hold completed.")),

            When(KitchenAccepted)
                .Publish(ctx => new CancelKitchenOrderCommand(
                    ctx.Saga.CorrelationId,
                    ctx.Saga.FailureReason ?? "Order was cancelled before the kitchen accepted it.")));
    }

    public State AwaitingPaymentHold { get; private set; } = null!;

    public State AwaitingKitchenConfirmation { get; private set; } = null!;

    public State CookingWaitingForDispatchWindow { get; private set; } = null!;

    public State MatchingCourier { get; private set; } = null!;

    public State Delivering { get; private set; } = null!;

    public State Completed { get; private set; } = null!;

    public State Cancelled { get; private set; } = null!;

    public Event<OrderSubmittedIntegrationEvent> OrderSubmitted { get; private set; } = null!;

    public Event<OrderCancelledByCustomerIntegrationEvent> OrderCancelledByCustomer { get; private set; } = null!;

    public Event<FundsHeldIntegrationEvent> FundsHeld { get; private set; } = null!;

    public Event<FundsHoldFailedIntegrationEvent> FundsHoldFailed { get; private set; } = null!;

    public Event<OrderAcceptedByKitchenIntegrationEvent> KitchenAccepted { get; private set; } = null!;

    public Event<KitchenRejectedOrderIntegrationEvent> KitchenRejected { get; private set; } = null!;

    public Event<CourierMatchingFailedIntegrationEvent> CourierMatchingFailed { get; private set; } = null!;

    public Event<OrderPickedUpIntegrationEvent> OrderPickedUp { get; private set; } = null!;

    public Event<OrderDeliveredIntegrationEvent> OrderDelivered { get; private set; } = null!;

    public Schedule<OrderSagaState, TriggerCourierDispatchTimeout> CourierDispatchSchedule { get; private set; } = null!;

    public Schedule<OrderSagaState, PaymentHoldTimeoutExpired> PaymentHoldTimeout { get; private set; } = null!;

    public Schedule<OrderSagaState, KitchenConfirmationTimeoutExpired> KitchenConfirmationTimeout { get; private set; } = null!;

    public Schedule<OrderSagaState, CourierPickupTimeoutExpired> CourierPickupTimeout { get; private set; } = null!;

    private static void MarkCancelled(OrderSagaState saga, string reason)
    {
        saga.FailureReason = reason;
        saga.Version++;
    }

    // Every state that calls this is reachable only after FundsHeld stored the hold id.
    private static ReleaseFundsHoldCommand ReleaseHold(OrderSagaState saga) =>
        new(saga.CorrelationId, saga.PaymentHoldId!.Value, saga.FailureReason!);
}
