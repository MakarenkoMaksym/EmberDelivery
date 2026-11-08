using EmberDelivery.BuildingBlocks.Domain.Results;

namespace EmberDelivery.Ordering.Domain.Aggregates.Orders;

public static class OrderErrors
{
    public static readonly Error EmptyBasket = Error.Validation(
        "Order.EmptyBasket",
        "An order must contain at least one item.");

    public static readonly Error InvalidMenuItemId = Error.Validation(
        "Order.InvalidMenuItemId",
        "Menu item identifier cannot be empty.");

    public static readonly Error EmptyMenuItemName = Error.Validation(
        "Order.EmptyMenuItemName",
        "Menu item name is required.");

    public static readonly Error AlreadyTerminalState = Error.Conflict(
        "Order.AlreadyTerminalState",
        "The order is already completed or cancelled and cannot be modified.");

    public static readonly Error ForbiddenCancellation = Error.Forbidden(
        "Order.ForbiddenCancellation",
        "You are not allowed to cancel an order that belongs to another customer.");

    public static Error NotFound(Guid orderId) => Error.NotFound(
        "Order.NotFound",
        $"Order with identifier '{orderId}' was not found.");

    public static Error CannotCancelInProgressOrder(OrderStatus currentStatus) => Error.Conflict(
        "Order.CannotCancelInProgressOrder",
        $"Customer cannot cancel the order because it is already in '{currentStatus}' state.");

    public static Error InvalidItemQuantity(int quantity) => Error.Validation(
        "Order.InvalidItemQuantity",
        $"Item quantity must be at least 1, but {quantity} was provided.");

    public static Error DuplicateModifier(Guid modifierId) => Error.Validation(
        "Order.DuplicateModifier",
        $"Modifier '{modifierId}' cannot be applied more than once to the same order line.");

    public static Error MenuItemUnavailable(Guid menuItemId) => Error.Conflict(
        "Order.MenuItemUnavailable",
        $"Menu item '{menuItemId}' is currently unavailable or in the stop-list.");

    public static Error ModifierUnavailable(Guid modifierId, string itemName) => Error.Conflict(
        "Order.ModifierUnavailable",
        $"Modifier '{modifierId}' is unavailable or not allowed for item '{itemName}'.");

    public static Error BelowMinimumBasket(decimal requiredAmount, decimal actualAmount) => Error.Conflict(
        "Order.BelowMinimumBasket",
        $"The restaurant requires a minimum basket of {requiredAmount:0.00}, but the current subtotal is {actualAmount:0.00}.");

    public static Error PriceChanged(decimal expectedAmount, decimal actualAmount) => Error.Conflict(
        "Order.PriceChanged",
        $"The total price changed from {expectedAmount:0.00} to {actualAmount:0.00} while you were checking out.");

    public static Error InvalidStateTransition(OrderStatus current, OrderStatus target) => Error.Conflict(
        "Order.InvalidStateTransition",
        $"Cannot transition order from '{current}' to '{target}'.");
}
