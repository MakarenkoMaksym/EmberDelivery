using EmberDelivery.BuildingBlocks.Application.Messaging;
using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.Ordering.Application.Orders.Commands.FailOrder;
using EmberDelivery.Ordering.Application.Orders.Commands.MarkOrderCompleted;
using EmberDelivery.Ordering.Application.Orders.Commands.MarkOrderCooking;
using EmberDelivery.Ordering.Application.Orders.Commands.MarkOrderDelivering;
using EmberDelivery.Ordering.Application.Orders.Commands.MarkOrderPaid;

using MassTransit;

using MediatR;

using static EmberDelivery.Ordering.IntegrationEvents.OrderSagaLifecycleEvents;

namespace EmberDelivery.Ordering.Api.Consumers;

public sealed class OrderSagaLifecycleConsumer(
    ISender sender,
    ILogger<OrderSagaLifecycleConsumer> logger) :
    IConsumer<OrderPaymentConfirmedIntegrationEvent>,
    IConsumer<OrderCookingStartedIntegrationEvent>,
    IConsumer<OrderDeliveryStartedIntegrationEvent>,
    IConsumer<OrderFulfillmentCompletedIntegrationEvent>,
    IConsumer<OrderFulfillmentFailedIntegrationEvent>
{
    public Task Consume(ConsumeContext<OrderPaymentConfirmedIntegrationEvent> context) =>
        SendAsync(context, context.Message.OrderId, new MarkOrderPaidCommand(context.Message.OrderId));

    public Task Consume(ConsumeContext<OrderCookingStartedIntegrationEvent> context) =>
        SendAsync(
            context,
            context.Message.OrderId,
            new MarkOrderCookingCommand(context.Message.OrderId, context.Message.EstimatedPrepMinutes));

    public Task Consume(ConsumeContext<OrderDeliveryStartedIntegrationEvent> context) =>
        SendAsync(
            context,
            context.Message.OrderId,
            new MarkOrderDeliveringCommand(context.Message.OrderId, context.Message.CourierId));

    public Task Consume(ConsumeContext<OrderFulfillmentCompletedIntegrationEvent> context) =>
        SendAsync(context, context.Message.OrderId, new MarkOrderCompletedCommand(context.Message.OrderId));

    public Task Consume(ConsumeContext<OrderFulfillmentFailedIntegrationEvent> context) =>
        SendAsync(
            context,
            context.Message.OrderId,
            new FailOrderCommand(context.Message.OrderId, context.Message.Reason));

    // A conflict means the aggregate already moved on (e.g. the customer cancelled while the saga advanced), which is an
    // expected race and safe to acknowledge. Anything else, notably a missing order that the outbox guarantees to exist,
    // is a contract violation that must fault the message instead of silently desynchronising the order from the saga.
    private async Task SendAsync<TMessage>(ConsumeContext<TMessage> context, Guid orderId, ICommand command)
        where TMessage : class
    {
        Result result = await sender.Send(command, context.CancellationToken);

        if (result.IsSuccess)
        {
            return;
        }

        if (result.Error.Type == ErrorType.Conflict)
        {
            logger.LogWarning(
                "Order {OrderId} rejected saga event {EventType}: {ErrorCode} {ErrorDescription}",
                orderId,
                typeof(TMessage).Name,
                result.Error.Code,
                result.Error.Description);
            return;
        }

        throw new InvalidOperationException(
            $"Order {orderId} could not apply saga event {typeof(TMessage).Name}: {result.Error.Code} {result.Error.Description}");
    }
}
