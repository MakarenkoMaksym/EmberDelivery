namespace EmberDelivery.BuildingBlocks.Infrastructure.Identity;

// Registered by every service so the same policy name means the same rule across the whole system.
public static class AuthorizationPolicies
{
    public const string RequireCustomer = "RequireCustomer";

    public const string RequireCourier = "RequireCourier";

    public const string RequireRestaurantManager = "RequireRestaurantManager";

    public const string RequireAdmin = "RequireAdmin";
}
