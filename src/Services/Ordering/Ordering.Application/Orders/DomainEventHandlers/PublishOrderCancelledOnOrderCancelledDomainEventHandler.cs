using EmberDelivery.BuildingBlocks.Application.Messaging;
using EmberDelivery.Ordering.Domain.Aggregates.Orders.Events;
using EmberDelivery.Ordering.IntegrationEvents;

namespace EmberDelivery.Ordering.Application.Orders.DomainEventHandlers;

internal sealed class PublishOrderCancelledOnOrderCancelledDomainEventHandler(
    IEventBus eventBus) : IDomainEventHandler<OrderCancelledDomainEvent>
{
    public async Task Handle(
        OrderCancelledDomainEvent notification,
        CancellationToken cancellationToken)
    {
        // Only notify the Saga when the cancellation was initiated by the customer via API.
        // System-initiated cancellations originate from the Saga itself.
        if (!notification.IsCustomerInitiated)
        {
            return;
        }

        OrderCancelledByCustomerIntegrationEvent integrationEvent = new(
            notification.OrderId,
            notification.Reason);

        await eventBus.PublishAsync(integrationEvent, cancellationToken);
    }
}
