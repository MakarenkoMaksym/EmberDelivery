using EmberDelivery.BuildingBlocks.Application.Abstractions;
using EmberDelivery.BuildingBlocks.Application.Messaging;
using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;

namespace EmberDelivery.Ordering.Application.Orders.Commands.MarkOrderDelivering;

public sealed record MarkOrderDeliveringCommand(
    Guid OrderId,
    Guid CourierId) : ICommand;

internal sealed class MarkOrderDeliveringCommandHandler(
    IOrderRepository orderRepository,
    IUnitOfWork unitOfWork) : ICommandHandler<MarkOrderDeliveringCommand>
{
    public async Task<Result> Handle(
        MarkOrderDeliveringCommand request,
        CancellationToken cancellationToken)
    {
        Order? order = await orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure(OrderErrors.NotFound(request.OrderId));
        }

        Result result = order.MarkAsDelivering(request.CourierId);
        if (result.IsFailure)
        {
            return result;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
