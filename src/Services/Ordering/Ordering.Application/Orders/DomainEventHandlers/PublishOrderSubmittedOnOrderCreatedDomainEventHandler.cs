using EmberDelivery.BuildingBlocks.Application.Messaging;
using EmberDelivery.Ordering.Domain.Aggregates.Orders.Events;
using EmberDelivery.Ordering.IntegrationEvents;

namespace EmberDelivery.Ordering.Application.Orders.DomainEventHandlers;

internal sealed class PublishOrderSubmittedOnOrderCreatedDomainEventHandler(
    IEventBus eventBus) : IDomainEventHandler<OrderCreatedDomainEvent>
{
    public async Task Handle(
        OrderCreatedDomainEvent notification,
        CancellationToken cancellationToken)
    {
        OrderSubmittedIntegrationEvent integrationEvent = new(
            notification.OrderId,
            notification.CustomerId,
            notification.RestaurantId,
            notification.TotalPrice.Amount,
            notification.TotalPrice.Currency);

        await eventBus.PublishAsync(integrationEvent, cancellationToken);
    }
}
