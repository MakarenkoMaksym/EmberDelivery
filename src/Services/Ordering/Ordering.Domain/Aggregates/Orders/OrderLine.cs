using EmberDelivery.BuildingBlocks.Domain.Abstractions;
using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.Ordering.Domain.ValueObjects;

namespace EmberDelivery.Ordering.Domain.Aggregates.Orders;

public sealed class OrderLine : Entity
{
    private readonly List<SelectedModifier> _modifiers = [];

    private OrderLine()
    {
    }

    private OrderLine(
        Guid id,
        Guid menuItemId,
        string name,
        Money baseUnitPrice,
        int quantity,
        IReadOnlyCollection<SelectedModifier> modifiers,
        Money lineTotal)
    {
        Id = id;
        MenuItemId = menuItemId;
        Name = name;
        BaseUnitPrice = baseUnitPrice;
        Quantity = quantity;
        LineTotal = lineTotal;
        _modifiers.AddRange(modifiers);
    }

    public Guid MenuItemId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public Money BaseUnitPrice { get; private set; }

    public int Quantity { get; private set; }

    public Money LineTotal { get; private set; }

    public IReadOnlyCollection<SelectedModifier> Modifiers => _modifiers;

    public static Result<OrderLine> Create(
        Guid menuItemId,
        string name,
        Money baseUnitPrice,
        int quantity,
        IReadOnlyCollection<SelectedModifier> modifiers)
    {
        if (menuItemId == Guid.Empty)
        {
            return Result.Failure<OrderLine>(OrderErrors.InvalidMenuItemId);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<OrderLine>(OrderErrors.EmptyMenuItemName);
        }

        if (quantity <= 0)
        {
            return Result.Failure<OrderLine>(OrderErrors.InvalidItemQuantity(quantity));
        }

        HashSet<Guid> seenModifierIds = new(modifiers.Count);
        var modifiersTotal = Money.Zero(baseUnitPrice.Currency);

        foreach (SelectedModifier modifier in modifiers)
        {
            // Prevent duplicate modifier IDs within a single line to avoid price ambiguity and invalid kitchen tickets.
            if (!seenModifierIds.Add(modifier.ModifierId))
            {
                return Result.Failure<OrderLine>(OrderErrors.DuplicateModifier(modifier.ModifierId));
            }

            modifiersTotal += modifier.Price;
        }

        Money lineTotal = (baseUnitPrice + modifiersTotal) * quantity;

        OrderLine line = new(
            Guid.NewGuid(),
            menuItemId,
            name.Trim(),
            baseUnitPrice,
            quantity,
            modifiers,
            lineTotal);

        return Result.Success(line);
    }
}
