using System.Text.Json;
using System.Text.Json.Serialization;

using EmberDelivery.Ordering.Domain.Pricing;
using EmberDelivery.Ordering.Domain.ValueObjects;

namespace EmberDelivery.Ordering.Infrastructure.Persistence.Projections;

internal static class MenuSnapshotJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new MoneyJsonConverter() }
    };

    public static RestaurantMenuSnapshot? Deserialize(string payloadJson) =>
        JsonSerializer.Deserialize<RestaurantMenuSnapshot>(payloadJson, Options);

    // System.Text.Json always uses a struct's implicit parameterless constructor, which would yield Money with a null
    // currency; routing through the real constructor keeps the domain free of serializer attributes and its invariants on.
    private sealed class MoneyJsonConverter : JsonConverter<Money>
    {
        public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Money must be serialized as an object with Amount and Currency.");
            }

            decimal? amount = null;
            string? currency = null;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string propertyName = reader.GetString() ?? string.Empty;
                reader.Read();

                if (propertyName.Equals(nameof(Money.Amount), StringComparison.OrdinalIgnoreCase))
                {
                    amount = reader.GetDecimal();
                }
                else if (propertyName.Equals(nameof(Money.Currency), StringComparison.OrdinalIgnoreCase))
                {
                    currency = reader.GetString();
                }
                else
                {
                    reader.Skip();
                }
            }

            if (amount is null || string.IsNullOrWhiteSpace(currency))
            {
                throw new JsonException("Money requires both Amount and Currency.");
            }

            return new Money(amount.Value, currency);
        }

        public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber(nameof(Money.Amount), value.Amount);
            writer.WriteString(nameof(Money.Currency), value.Currency);
            writer.WriteEndObject();
        }
    }
}
