using EmberDelivery.Ordering.Application.Abstractions;
using EmberDelivery.Ordering.Application.Orders.Queries.GetOrderById;

using Microsoft.EntityFrameworkCore;

namespace EmberDelivery.Ordering.Infrastructure.Persistence.ReadServices;

internal sealed class OrderReadService(OrderingDbContext dbContext) : IOrderReadService
{
    // Projecting straight to the response reads only the needed columns and skips aggregate materialization entirely.
    public Task<OrderResponse?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return dbContext.Orders
            .AsNoTracking()
            .Where(order => order.Id == orderId)
            .Select(order => new OrderResponse(
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
                order.Lines
                    .Select(line => new OrderLineResponse(
                        line.Id,
                        line.MenuItemId,
                        line.Name,
                        line.BaseUnitPrice.Amount,
                        line.Quantity,
                        line.LineTotal.Amount,
                        line.Modifiers
                            .Select(modifier => new OrderModifierResponse(
                                modifier.ModifierId,
                                modifier.Name,
                                modifier.PriceAmount))
                            .ToList()))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
