namespace EmberDelivery.BuildingBlocks.Infrastructure.Identity;

public sealed class KeycloakAuthOptions
{
    public const string SectionName = "Authentication:Keycloak";

    // Aspire resource name; the metadata address is resolved through service discovery under this name.
    public string ServiceName { get; set; } = "keycloak";

    public string Realm { get; set; } = "EmberDelivery";

    public string Audience { get; set; } = "ember-frontend";

    public bool RequireHttpsMetadata { get; set; }

    // Left null by default because the configuration binder appends to pre-populated arrays instead of replacing them.
    public string[]? ValidIssuers { get; set; }

    // Tokens are issued for the browser-facing Keycloak URL, which differs from the internal discovery address.
    public IReadOnlyList<string> GetValidIssuers() =>
        ValidIssuers is { Length: > 0 }
            ? ValidIssuers
            :
            [
                $"http://localhost:8080/realms/{Realm}",
                $"https://localhost:8080/realms/{Realm}"
            ];
}
