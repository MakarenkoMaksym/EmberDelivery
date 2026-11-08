using EmberDelivery.BuildingBlocks.Infrastructure.Presentation;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace EmberDelivery.Ordering.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWebApiServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();
        services.AddOpenApi();

        return services;
    }

    public static IServiceCollection AddKeycloakAuth(this IServiceCollection services)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddKeycloakJwtBearer("keycloak", realm: "EmberDelivery", options =>
        {
            options.Audience = "ember-frontend";
            options.RequireHttpsMetadata = false;
            options.IncludeErrorDetails = true;
            options.MapInboundClaims = false;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = true,
                ValidAudience = "ember-frontend",
                RoleClaimType = "role",
                NameClaimType = "preferred_username",
                ValidateIssuer = true,
                ValidIssuers =
                [
                    "http://localhost:8080/realms/EmberDelivery",
                    "https://localhost:8080/realms/EmberDelivery"
                ]
            };
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireCustomer", policy => policy.RequireRole("Customer"));
            options.AddPolicy("RequireCourier", policy => policy.RequireRole("Courier"));
            options.AddPolicy("RequireRestaurantManager", policy => policy.RequireRole("RestaurantManager"));
            options.AddPolicy("RequireAdmin", policy => policy.RequireRole("Admin"));
        });

        return services;
    }
}
