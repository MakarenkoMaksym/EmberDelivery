namespace EmberDelivery.Ordering.IntegrationTests.Infrastructure;

// Must mirror the role names required by the authorization policies in AddKeycloakAuth; role checks are case-sensitive.
public static class TestRoles
{
    public const string Customer = "Customer";
    public const string Admin = "Admin";
}
