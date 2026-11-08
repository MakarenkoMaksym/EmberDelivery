namespace EmberDelivery.Ordering.Domain.Aggregates.Orders;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(Order order);
}
