using EmberDelivery.BuildingBlocks.Application.Messaging;

using MassTransit;

namespace EmberDelivery.BuildingBlocks.Infrastructure.Messaging;

public sealed class MassTransitEventBus(IPublishEndpoint publishEndpoint) : IEventBus
{
    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class
    {
        // When EF Core Outbox is enabled on the bus, Publish writes to OutboxMessages in the active DbContext transaction.
        return publishEndpoint.Publish(message, cancellationToken);
    }
}
