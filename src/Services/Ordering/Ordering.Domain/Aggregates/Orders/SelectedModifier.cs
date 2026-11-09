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
        PriceAmount = price.Amount;
        PriceCurrency = price.Currency;
    }

    // EF Core supports neither complex types inside owned types nor owned value types, so the persisted state is
    // the flat amount/currency pair; materialization goes through this constructor and fills the read-only fields.
    private SelectedModifier()
    {
        Name = string.Empty;
        PriceCurrency = string.Empty;
    }

    public Guid ModifierId { get; }

    public string Name { get; }

    public Money Price => new(PriceAmount, PriceCurrency);

    private decimal PriceAmount { get; }

    private string PriceCurrency { get; }
}
