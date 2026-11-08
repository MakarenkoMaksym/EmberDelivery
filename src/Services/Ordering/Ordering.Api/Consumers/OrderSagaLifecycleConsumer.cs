using EmberDelivery.Ordering.Application.Orders.Commands.FailOrder;
using EmberDelivery.Ordering.Application.Orders.Commands.MarkOrderCompleted;
using EmberDelivery.Ordering.Application.Orders.Commands.MarkOrderCooking;
using EmberDelivery.Ordering.Application.Orders.Commands.MarkOrderDelivering;
using EmberDelivery.Ordering.Application.Orders.Commands.MarkOrderPaid;

using MassTransit;

using MediatR;

using static EmberDelivery.Ordering.IntegrationEvents.OrderSagaLifecycleEvents;

namespace EmberDelivery.Ordering.Api.Consumers;

public sealed class OrderSagaLifecycleConsumer(ISender sender) :
    IConsumer<OrderPaymentConfirmedIntegrationEvent>,
    IConsumer<OrderCookingStartedIntegrationEvent>,
    IConsumer<OrderDeliveryStartedIntegrationEvent>,
    IConsumer<OrderFulfillmentCompletedIntegrationEvent>,
    IConsumer<OrderFulfillmentFailedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<OrderPaymentConfirmedIntegrationEvent> context)
    {
        await sender.Send(new MarkOrderPaidCommand(context.Message.OrderId), context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<OrderCookingStartedIntegrationEvent> context)
    {
        await sender.Send(
            new MarkOrderCookingCommand(context.Message.OrderId, context.Message.EstimatedPrepMinutes),
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<OrderDeliveryStartedIntegrationEvent> context)
    {
        await sender.Send(
            new MarkOrderDeliveringCommand(context.Message.OrderId, context.Message.CourierId),
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<OrderFulfillmentCompletedIntegrationEvent> context)
    {
        await sender.Send(new MarkOrderCompletedCommand(context.Message.OrderId), context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<OrderFulfillmentFailedIntegrationEvent> context)
    {
        await sender.Send(
            new FailOrderCommand(context.Message.OrderId, context.Message.Reason),
            context.CancellationToken);
    }
}
