using MassTransit;

namespace EmberDelivery.Ordering.Application.Sagas.OrderFulfillment;

public sealed class OrderSagaState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }

    public string CurrentState { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    public Guid RestaurantId { get; set; }

    public decimal TotalAmount { get; set; }

    public string Currency { get; set; } = string.Empty;

    public Guid? PaymentHoldId { get; set; }

    public Guid? AssignedCourierId { get; set; }

    public string? FailureReason { get; set; }

    public Guid? CourierDispatchScheduleTokenId { get; set; }

    public uint Version { get; set; } = 1;
}
