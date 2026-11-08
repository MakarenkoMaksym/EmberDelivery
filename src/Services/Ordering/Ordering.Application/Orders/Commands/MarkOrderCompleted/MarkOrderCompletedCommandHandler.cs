using EmberDelivery.BuildingBlocks.Application.Abstractions;
using EmberDelivery.BuildingBlocks.Application.Messaging;
using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;

namespace EmberDelivery.Ordering.Application.Orders.Commands.MarkOrderCompleted;

public sealed record MarkOrderCompletedCommand(Guid OrderId) : ICommand;

internal sealed class MarkOrderCompletedCommandHandler(
    IOrderRepository orderRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<MarkOrderCompletedCommand>
{
    public async Task<Result> Handle(
        MarkOrderCompletedCommand request,
        CancellationToken cancellationToken)
    {
        Order? order = await orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure(OrderErrors.NotFound(request.OrderId));
        }

        Result result = order.MarkAsCompleted(timeProvider);
        if (result.IsFailure)
        {
            return result;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
