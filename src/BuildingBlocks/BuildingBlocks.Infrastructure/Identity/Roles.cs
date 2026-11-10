namespace EmberDelivery.BuildingBlocks.Infrastructure.Identity;

// Must match the realm roles defined in Keycloak exactly; role checks are case-sensitive.
public static class Roles
{
    public const string Customer = "Customer";

    public const string Courier = "Courier";

    public const string RestaurantManager = "RestaurantManager";

    public const string Admin = "Admin";
}
