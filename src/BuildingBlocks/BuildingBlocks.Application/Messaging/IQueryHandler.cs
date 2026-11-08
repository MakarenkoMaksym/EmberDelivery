using EmberDelivery.BuildingBlocks.Domain.Results;

using MediatR;

namespace EmberDelivery.BuildingBlocks.Application.Messaging;

public interface IQueryHandler<TQuery, TResponse>
    : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>
{
}
