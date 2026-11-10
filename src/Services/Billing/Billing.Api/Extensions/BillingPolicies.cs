namespace EmberDelivery.Billing.Api.Extensions;

public static class BillingPolicies
{
    // Customers read their own payments and admins read any; ownership itself is checked per resource in PaymentQuery.
    public const string CustomerOrAdmin = "RequireCustomerOrAdmin";
}
