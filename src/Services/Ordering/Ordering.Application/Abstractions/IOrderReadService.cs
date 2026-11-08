using EmberDelivery.Ordering.Application.Orders.Queries.GetOrderById;

namespace EmberDelivery.Ordering.Application.Abstractions;

public interface IOrderReadService
{
    Task<OrderResponse?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default);
}
