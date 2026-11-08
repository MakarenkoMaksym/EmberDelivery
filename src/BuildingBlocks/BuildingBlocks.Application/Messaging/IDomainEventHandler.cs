using EmberDelivery.BuildingBlocks.Domain.Abstractions;

using MediatR;

namespace EmberDelivery.BuildingBlocks.Application.Messaging;

public interface IDomainEventHandler<in TDomainEvent> : INotificationHandler<TDomainEvent>
    where TDomainEvent : IDomainEvent
{
}
