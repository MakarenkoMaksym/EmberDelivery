using EmberDelivery.BuildingBlocks.Application.Abstractions;
using EmberDelivery.BuildingBlocks.Application.Messaging;
using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;

namespace EmberDelivery.Ordering.Application.Orders.Commands.FailOrder;

public sealed record FailOrderCommand(
    Guid OrderId,
    string Reason) : ICommand;

internal sealed class FailOrderCommandHandler(
    IOrderRepository orderRepository,
    IUnitOfWork unitOfWork) : ICommandHandler<FailOrderCommand>
{
    public async Task<Result> Handle(
        FailOrderCommand request,
        CancellationToken cancellationToken)
    {
        Order? order = await orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure(OrderErrors.NotFound(request.OrderId));
        }

        Result result = order.CancelBySystem(request.Reason);
        if (result.IsFailure)
        {
            return result;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
