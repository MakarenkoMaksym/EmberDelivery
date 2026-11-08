using EmberDelivery.BuildingBlocks.Domain.Abstractions;
using EmberDelivery.Ordering.Domain.ValueObjects;

namespace EmberDelivery.Ordering.Domain.Aggregates.Orders;

public sealed record SelectedModifier : IValueObject
{
    public SelectedModifier(Guid modifierId, string name, Money price)
    {
        if (modifierId == Guid.Empty)
        {
            throw new ArgumentException("Modifier identifier cannot be empty.", nameof(modifierId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Modifier name is required.", nameof(name));
        }

        ModifierId = modifierId;
        Name = name.Trim();
        Price = price;
    }

    public Guid ModifierId { get; }

    public string Name { get; }

    public Money Price { get; }
}
