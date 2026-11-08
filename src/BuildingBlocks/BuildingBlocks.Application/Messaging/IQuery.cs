using EmberDelivery.BuildingBlocks.Domain.Results;

using MediatR;

namespace EmberDelivery.BuildingBlocks.Application.Messaging;

public interface IQuery<TResponse> : IRequest<Result<TResponse>>
{
}
