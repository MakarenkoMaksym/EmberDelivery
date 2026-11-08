using EmberDelivery.Ordering.Domain.Aggregates.Orders;

using Microsoft.EntityFrameworkCore;

namespace EmberDelivery.Ordering.Infrastructure.Persistence.Repositories;

internal sealed class OrderRepository(OrderingDbContext dbContext) : IOrderRepository
{
    public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return dbContext.Orders
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public void Add(Order order)
    {
        dbContext.Orders.Add(order);
    }
}
