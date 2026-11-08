namespace EmberDelivery.Ordering.Domain.Aggregates.Orders;

public enum OrderStatus
{
    Submitted = 1,
    Paid = 2,
    Cooking = 3,
    Delivering = 4,
    Completed = 5,
    Cancelled = 6
}
