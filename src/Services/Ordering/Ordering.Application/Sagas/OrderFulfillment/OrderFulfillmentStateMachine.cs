using EmberDelivery.Ordering.IntegrationEvents;

using MassTransit;

using static EmberDelivery.Billing.IntegrationEvents.PaymentContracts;
using static EmberDelivery.Dispatch.IntegrationEvents.CourierContracts;
using static EmberDelivery.Kitchen.IntegrationEvents.KitchenSlotContracts;
using static EmberDelivery.Ordering.IntegrationEvents.OrderSagaLifecycleEvents;
using static EmberDelivery.Ordering.IntegrationEvents.OrderSagaTimeouts;

namespace EmberDelivery.Ordering.Application.Sagas.OrderFulfillment;

public sealed class OrderFulfillmentStateMachine : MassTransitStateMachine<OrderSagaState>
{
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
        Event(() => OrderPickedUp, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => OrderDelivered, x => x.CorrelateById(ctx => ctx.Message.OrderId));

        Schedule(
            () => CourierDispatchSchedule,
            instance => instance.CourierDispatchScheduleTokenId,
            cfg =>
            {
                cfg.Received = e => e.CorrelateById(ctx => ctx.Message.OrderId);
            });

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
                .TransitionTo(AwaitingPaymentHold));

        // Phase 2: Payment hold result from Billing (or early customer cancellation).
        During(AwaitingPaymentHold,
            When(FundsHeld)
                .Then(ctx =>
                {
                    ctx.Saga.PaymentHoldId = ctx.Message.PaymentHoldId;
                    ctx.Saga.Version++;
                })
                .Publish(ctx => new OrderPaymentConfirmedIntegrationEvent(ctx.Saga.CorrelationId))
                .Publish(ctx => new ConfirmKitchenCapacityCommand(
                    ctx.Saga.CorrelationId,
                    ctx.Saga.RestaurantId))
                .TransitionTo(AwaitingKitchenConfirmation),

            When(FundsHoldFailed)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = ctx.Message.Reason;
                    ctx.Saga.Version++;
                })
                .Publish(ctx => new OrderFulfillmentFailedIntegrationEvent(
                    ctx.Saga.CorrelationId,
                    ctx.Message.Reason))
                .TransitionTo(Cancelled),

            When(OrderCancelledByCustomer)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = ctx.Message.Reason;
                    ctx.Saga.Version++;
                })
                .TransitionTo(Cancelled));

        // Phase 3: Kitchen capacity confirmation, kitchen rejection, or customer cancellation while in Paid state.
        During(AwaitingKitchenConfirmation,
            Ignore(FundsHeld),

            When(KitchenAccepted)
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
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = ctx.Message.Reason;
                    ctx.Saga.Version++;
                })
                .Publish(ctx => new ReleaseFundsHoldCommand(
                    ctx.Saga.CorrelationId,
                    ctx.Saga.PaymentHoldId!.Value,
                    ctx.Message.Reason))
                .Publish(ctx => new OrderFulfillmentFailedIntegrationEvent(
                    ctx.Saga.CorrelationId,
                    ctx.Message.Reason))
                .TransitionTo(Cancelled),

            When(OrderCancelledByCustomer)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = ctx.Message.Reason;
                    ctx.Saga.Version++;
                })
                .Publish(ctx => new ReleaseFundsHoldCommand(
                    ctx.Saga.CorrelationId,
                    ctx.Saga.PaymentHoldId!.Value,
                    ctx.Message.Reason))
                .TransitionTo(Cancelled));

        // Phase 4: Cooking in progress; waiting for Just-in-Time courier dispatch window.
        During(CookingWaitingForDispatchWindow,
            Ignore(KitchenAccepted),
            Ignore(OrderCancelledByCustomer),

            When(CourierDispatchSchedule.Received)
                .Then(ctx => ctx.Saga.Version++)
                .Publish(ctx => new StartCourierMatchingCommand(
                    ctx.Saga.CorrelationId,
                    ctx.Saga.RestaurantId,
                    ctx.Saga.CustomerId))
                .TransitionTo(MatchingCourier),

            When(KitchenRejected)
                .Unschedule(CourierDispatchSchedule)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = ctx.Message.Reason;
                    ctx.Saga.Version++;
                })
                .Publish(ctx => new ReleaseFundsHoldCommand(
                    ctx.Saga.CorrelationId,
                    ctx.Saga.PaymentHoldId!.Value,
                    ctx.Message.Reason))
                .Publish(ctx => new OrderFulfillmentFailedIntegrationEvent(
                    ctx.Saga.CorrelationId,
                    ctx.Message.Reason))
                .TransitionTo(Cancelled));

        // Phase 5: Courier picks up the hot food from the kitchen.
        During(MatchingCourier,
            Ignore(OrderCancelledByCustomer),

            When(OrderPickedUp)
                .Then(ctx =>
                {
                    ctx.Saga.AssignedCourierId = ctx.Message.CourierId;
                    ctx.Saga.Version++;
                })
                .Publish(ctx => new OrderDeliveryStartedIntegrationEvent(
                    ctx.Saga.CorrelationId,
                    ctx.Message.CourierId))
                .TransitionTo(Delivering));

        // Phase 6: Courier hands order to customer -> capture funds and complete.
        During(Delivering,
            Ignore(OrderPickedUp),
            Ignore(OrderCancelledByCustomer),

            When(OrderDelivered)
                .Then(ctx => ctx.Saga.Version++)
                .Publish(ctx => new CaptureFundsCommand(
                    ctx.Saga.CorrelationId,
                    ctx.Saga.PaymentHoldId!.Value))
                .Publish(ctx => new OrderFulfillmentCompletedIntegrationEvent(
                    ctx.Saga.CorrelationId,
                    ctx.Message.CourierId))
                .TransitionTo(Completed));

        // If Billing finishes holding funds AFTER the customer already cancelled the order,
        // immediately issue a compensating ReleaseFundsHoldCommand so the customer's money is never stranded.
        During(Cancelled,
            Ignore(OrderCancelledByCustomer),
            Ignore(FundsHoldFailed),
            Ignore(KitchenRejected),

            When(FundsHeld)
                .Then(ctx =>
                {
                    ctx.Saga.PaymentHoldId = ctx.Message.PaymentHoldId;
                    ctx.Saga.Version++;
                })
                .Publish(ctx => new ReleaseFundsHoldCommand(
                    ctx.Saga.CorrelationId,
                    ctx.Message.PaymentHoldId,
                    ctx.Saga.FailureReason ?? "Order was cancelled before payment hold completed.")));
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

    public Event<OrderPickedUpIntegrationEvent> OrderPickedUp { get; private set; } = null!;

    public Event<OrderDeliveredIntegrationEvent> OrderDelivered { get; private set; } = null!;

    public Schedule<OrderSagaState, TriggerCourierDispatchTimeout> CourierDispatchSchedule { get; private set; } = null!;
}
