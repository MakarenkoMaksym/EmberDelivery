using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;
using EmberDelivery.Ordering.Domain.ValueObjects;

namespace EmberDelivery.Ordering.Domain.Pricing;

public sealed record MenuModifierSnapshot(
    Guid ModifierId,
    string Name,
    Money Price,
    bool IsAvailable);

public sealed record MenuItemSnapshot(
    Guid MenuItemId,
    string Name,
    Money BasePrice,
    bool IsAvailable,
    IReadOnlyDictionary<Guid, MenuModifierSnapshot> AllowedModifiers);

public readonly record struct OrderLineSpec(
    Guid MenuItemId,
    int Quantity,
    IReadOnlyCollection<Guid> SelectedModifierIds);

public sealed class RestaurantMenuSnapshot(
    Guid restaurantId,
    bool isAcceptingOrders,
    Money minimumBasketRequirement,
    Money baseDeliveryFee,
    Money? freeDeliveryThreshold,
    IReadOnlyDictionary<Guid, MenuItemSnapshot> items)
{
    public Guid RestaurantId { get; } = restaurantId;

    public bool IsAcceptingOrders { get; } = isAcceptingOrders;

    public Money MinimumBasketRequirement { get; } = minimumBasketRequirement;

    public Money BaseDeliveryFee { get; } = baseDeliveryFee;

    public Money? FreeDeliveryThreshold { get; } = freeDeliveryThreshold;

    public IReadOnlyDictionary<Guid, MenuItemSnapshot> Items { get; } = items;

    public Result<IReadOnlyList<OrderLine>> CreateOrderLines(IReadOnlyCollection<OrderLineSpec> specs)
    {
        if (specs.Count == 0)
        {
            return Result.Failure<IReadOnlyList<OrderLine>>(OrderErrors.EmptyBasket);
        }

        List<OrderLine> lines = new(specs.Count);

        foreach (OrderLineSpec spec in specs)
        {
            Result<OrderLine> lineResult = CreateOrderLine(
                spec.MenuItemId,
                spec.Quantity,
                spec.SelectedModifierIds);

            if (lineResult.IsFailure)
            {
                return Result.Failure<IReadOnlyList<OrderLine>>(lineResult.Error);
            }

            lines.Add(lineResult.Value);
        }

        return Result.Success<IReadOnlyList<OrderLine>>(lines);
    }

    public Result<OrderLine> CreateOrderLine(
        Guid menuItemId,
        int quantity,
        IReadOnlyCollection<Guid> selectedModifierIds)
    {
        if (!Items.TryGetValue(menuItemId, out MenuItemSnapshot? menuItem) || !menuItem.IsAvailable)
        {
            return Result.Failure<OrderLine>(OrderErrors.MenuItemUnavailable(menuItemId));
        }

        List<SelectedModifier> modifiers = new(selectedModifierIds.Count);

        foreach (Guid modifierId in selectedModifierIds)
        {
            if (!menuItem.AllowedModifiers.TryGetValue(modifierId, out MenuModifierSnapshot? modSnapshot) ||
                !modSnapshot.IsAvailable)
            {
                return Result.Failure<OrderLine>(
                    OrderErrors.ModifierUnavailable(modifierId, menuItem.Name));
            }

            modifiers.Add(new SelectedModifier(modSnapshot.ModifierId, modSnapshot.Name, modSnapshot.Price));
        }

        return OrderLine.Create(
            menuItem.MenuItemId,
            menuItem.Name,
            menuItem.BasePrice,
            quantity,
            modifiers);
    }
}
