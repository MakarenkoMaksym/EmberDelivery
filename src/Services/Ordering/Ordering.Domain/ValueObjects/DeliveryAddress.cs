using EmberDelivery.BuildingBlocks.Domain.Abstractions;
using EmberDelivery.BuildingBlocks.Domain.Results;

namespace EmberDelivery.Ordering.Domain.ValueObjects;

public sealed record DeliveryAddress : IValueObject
{
    private const double MinLatitude = -90.0;
    private const double MaxLatitude = 90.0;
    private const double MinLongitude = -180.0;
    private const double MaxLongitude = 180.0;

    private DeliveryAddress()
    {
    }

    private DeliveryAddress(
        string street,
        string building,
        string? apartment,
        string? deliveryNotes,
        double latitude,
        double longitude)
    {
        Street = street;
        Building = building;
        Apartment = apartment;
        DeliveryNotes = deliveryNotes;
        Latitude = latitude;
        Longitude = longitude;
    }

    public string Street { get; } = string.Empty;

    public string Building { get; } = string.Empty;

    public string? Apartment { get; }

    public string? DeliveryNotes { get; }

    public double Latitude { get; }

    public double Longitude { get; }

    public static Result<DeliveryAddress> Create(
        string street,
        string building,
        double latitude,
        double longitude,
        string? apartment = null,
        string? deliveryNotes = null)
    {
        if (string.IsNullOrWhiteSpace(street))
        {
            return Result.Failure<DeliveryAddress>(
                Error.Validation("DeliveryAddress.EmptyStreet", "Street name is required."));
        }

        if (string.IsNullOrWhiteSpace(building))
        {
            return Result.Failure<DeliveryAddress>(
                Error.Validation("DeliveryAddress.EmptyBuilding", "Building number is required."));
        }

        if (double.IsNaN(latitude) || latitude < MinLatitude || latitude > MaxLatitude)
        {
            return Result.Failure<DeliveryAddress>(
                Error.Validation("DeliveryAddress.InvalidLatitude", "Latitude must be between -90 and 90 degrees."));
        }

        if (double.IsNaN(longitude) || longitude < MinLongitude || longitude > MaxLongitude)
        {
            return Result.Failure<DeliveryAddress>(
                Error.Validation("DeliveryAddress.InvalidLongitude", "Longitude must be between -180 and 180 degrees."));
        }

        DeliveryAddress address = new(
            street.Trim(),
            building.Trim(),
             string.IsNullOrWhiteSpace(apartment) ? null : apartment.Trim(),
            string.IsNullOrWhiteSpace(deliveryNotes) ? null : deliveryNotes.Trim(),
            latitude,
            longitude);

        return Result.Success(address);
    }
}
