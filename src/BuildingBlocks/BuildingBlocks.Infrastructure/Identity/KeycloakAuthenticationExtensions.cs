using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace EmberDelivery.BuildingBlocks.Infrastructure.Identity;

public static class KeycloakAuthenticationExtensions
{
    public static IHostApplicationBuilder AddKeycloakAuth(
        this IHostApplicationBuilder builder,
        Action<AuthorizationBuilder>? configureAuthorization = null)
    {
        KeycloakAuthOptions keycloak = builder.Configuration
            .GetSection(KeycloakAuthOptions.SectionName)
            .Get<KeycloakAuthOptions>() ?? new KeycloakAuthOptions();

        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddKeycloakJwtBearer(keycloak.ServiceName, realm: keycloak.Realm, options =>
        {
            options.Audience = keycloak.Audience;
            options.RequireHttpsMetadata = keycloak.RequireHttpsMetadata;
            options.IncludeErrorDetails = true;

            // Keeps Keycloak claim names ("sub", "role") intact so CurrentUserService and role policies read them as-is.
            options.MapInboundClaims = false;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = true,
                ValidAudience = keycloak.Audience,
                RoleClaimType = "role",
                NameClaimType = "preferred_username",
                ValidateIssuer = true,
                ValidIssuers = keycloak.GetValidIssuers()
            };
        });

        AuthorizationBuilder authorization = builder.Services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.RequireCustomer, policy => policy.RequireRole(Roles.Customer))
            .AddPolicy(AuthorizationPolicies.RequireCourier, policy => policy.RequireRole(Roles.Courier))
            .AddPolicy(AuthorizationPolicies.RequireRestaurantManager, policy => policy.RequireRole(Roles.RestaurantManager))
            .AddPolicy(AuthorizationPolicies.RequireAdmin, policy => policy.RequireRole(Roles.Admin));

        configureAuthorization?.Invoke(authorization);

        return builder;
    }
}
