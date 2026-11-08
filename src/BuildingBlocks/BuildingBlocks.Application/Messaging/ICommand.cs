using EmberDelivery.BuildingBlocks.Domain.Results;

using MediatR;

namespace EmberDelivery.BuildingBlocks.Application.Messaging;

public interface ICommand : IRequest<Result>
{
}

public interface ICommand<TResponse> : IRequest<Result<TResponse>>
{
}
