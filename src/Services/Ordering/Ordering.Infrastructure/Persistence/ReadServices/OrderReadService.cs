using EmberDelivery.Ordering.Application.Abstractions;
using EmberDelivery.Ordering.Application.Orders.Queries.GetOrderById;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;

using Microsoft.EntityFrameworkCore;

namespace EmberDelivery.Ordering.Infrastructure.Persistence.ReadServices;

internal sealed class OrderReadService(OrderingDbContext dbContext) : IOrderReadService
{
    public async Task<OrderResponse?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        Order? order = await dbContext.Orders
            .AsNoTracking()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == orderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var lines = order.Lines
            .Select(line => new OrderLineResponse(
                line.Id,
                line.MenuItemId,
                line.Name,
                line.BaseUnitPrice.Amount,
                line.Quantity,
                line.LineTotal.Amount,
                line.Modifiers
                    .Select(m => new OrderModifierResponse(m.ModifierId, m.Name, m.Price.Amount))
                    .ToList()))
            .ToList();

        return new OrderResponse(
            order.Id,
            order.CustomerId,
            order.RestaurantId,
            order.Status.ToString(),
            order.DeliveryAddress.Street,
            order.DeliveryAddress.Building,
            order.DeliveryAddress.Apartment,
            order.DeliveryAddress.Latitude,
            order.DeliveryAddress.Longitude,
            order.ItemsSubtotal.Amount,
            order.DeliveryFee.Amount,
            order.DiscountAmount.Amount,
            order.TotalPrice.Amount,
            order.TotalPrice.Currency,
            order.AppliedCouponCode,
            order.AssignedCourierId,
            order.CancellationReason,
            order.CreatedAtUtc,
            order.EstimatedDeliveryAtUtc,
            order.CompletedAtUtc,
            lines);
    }
}
