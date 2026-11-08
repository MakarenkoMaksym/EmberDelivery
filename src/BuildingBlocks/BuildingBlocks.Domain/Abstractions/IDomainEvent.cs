using MediatR;

namespace EmberDelivery.BuildingBlocks.Domain.Abstractions;

// Inherits from INotification (provided by the zero-dependency MediatR.Contracts package)
// to allow direct in-process dispatching in UnitOfWork without reflection or wrapper allocations.
public interface IDomainEvent : INotification;
