using System.Text.Json;

using EmberDelivery.Ordering.Application.Orders.Commands.CreateOrder;
using EmberDelivery.Ordering.Domain.Pricing;
using EmberDelivery.Ordering.Domain.ValueObjects;

using static EmberDelivery.Ordering.Api.Endpoints.Orders.CreateOrderEndpoint;

namespace EmberDelivery.Ordering.IntegrationTests.TestData;

// Prices are hardcoded so every expected total in the tests can be verified by hand, never recomputed from domain code.
public static class TestMenu
{
    public const decimal BurgerPrice = 150m;
    public const decimal CheesePrice = 25m;
    public const decimal FriesPrice = 60m;
    public const decimal MinimumBasket = 200m;
    public const decimal BaseDeliveryFee = 50m;
    public const decimal FreeDeliveryThreshold = 1000m;

    // 2 x (Burger 150 + Cheese 25) = 350 subtotal, below the free-delivery threshold, so 50 delivery is added.
    public const decimal StandardBasketSubtotal = 350m;
    public const decimal StandardBasketTotal = 400m;

    public static readonly Guid BurgerId = Guid.Parse("0b4c6f7e-1a2b-4c3d-8e9f-000000000001");
    public static readonly Guid CheeseId = Guid.Parse("0b4c6f7e-1a2b-4c3d-8e9f-000000000002");
    public static readonly Guid FriesId = Guid.Parse("0b4c6f7e-1a2b-4c3d-8e9f-000000000003");
    public static readonly Guid SoldOutSoupId = Guid.Parse("0b4c6f7e-1a2b-4c3d-8e9f-000000000004");

    public static RestaurantMenuSnapshot CreateSnapshot(
        Guid restaurantId,
        bool isAcceptingOrders = true,
        string currency = "UAH")
    {
        MenuModifierSnapshot cheese = new(CheeseId, "Cheddar", new Money(CheesePrice, currency), IsAvailable: true);

        Dictionary<Guid, MenuItemSnapshot> items = new()
        {
            [BurgerId] = new MenuItemSnapshot(
                BurgerId,
                "Ember Burger",
                new Money(BurgerPrice, currency),
                IsAvailable: true,
                new Dictionary<Guid, MenuModifierSnapshot> { [CheeseId] = cheese }),
            [FriesId] = new MenuItemSnapshot(
                FriesId,
                "Fries",
                new Money(FriesPrice, currency),
                IsAvailable: true,
                new Dictionary<Guid, MenuModifierSnapshot>()),
            [SoldOutSoupId] = new MenuItemSnapshot(
                SoldOutSoupId,
                "Borscht",
                new Money(90m, currency),
                IsAvailable: false,
                new Dictionary<Guid, MenuModifierSnapshot>())
        };

        return new RestaurantMenuSnapshot(
            restaurantId,
            isAcceptingOrders,
            new Money(MinimumBasket, currency),
            new Money(BaseDeliveryFee, currency),
            new Money(FreeDeliveryThreshold, currency),
            items);
    }

    public static string CreatePayloadJson(Guid restaurantId, bool isAcceptingOrders = true, string currency = "UAH") =>
        JsonSerializer.Serialize(CreateSnapshot(restaurantId, isAcceptingOrders, currency));

    public static IReadOnlyList<OrderItemRequest> StandardBasket() =>
        [new OrderItemRequest(BurgerId, Quantity: 2, SelectedModifierIds: [CheeseId])];

    public static CreateOrderRequest StandardOrderRequest(
        Guid restaurantId,
        decimal expectedTotalAmount = StandardBasketTotal,
        string? couponCode = null,
        string currency = "UAH") =>
        new(
            restaurantId,
            Street: "Khreshchatyk",
            Building: "22",
            Latitude: 50.4501,
            Longitude: 30.5234,
            Items: StandardBasket(),
            CouponCode: couponCode,
            ExpectedTotalAmount: expectedTotalAmount,
            Currency: currency);
}
