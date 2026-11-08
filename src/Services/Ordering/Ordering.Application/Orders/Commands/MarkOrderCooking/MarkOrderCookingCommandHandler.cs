using EmberDelivery.BuildingBlocks.Application.Abstractions;
using EmberDelivery.BuildingBlocks.Application.Messaging;
using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;

namespace EmberDelivery.Ordering.Application.Orders.Commands.MarkOrderCooking;

public sealed record MarkOrderCookingCommand(
    Guid OrderId,
    int EstimatedPrepMinutes) : ICommand;

internal sealed class MarkOrderCookingCommandHandler(
    IOrderRepository orderRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<MarkOrderCookingCommand>
{
    public async Task<Result> Handle(
        MarkOrderCookingCommand request,
        CancellationToken cancellationToken)
    {
        Order? order = await orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure(OrderErrors.NotFound(request.OrderId));
        }

        Result result = order.MarkAsCooking(request.EstimatedPrepMinutes, timeProvider);
        if (result.IsFailure)
        {
            return result;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
