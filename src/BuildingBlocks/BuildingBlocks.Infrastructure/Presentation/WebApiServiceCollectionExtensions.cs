using EmberDelivery.BuildingBlocks.Application.Abstractions.Identity;
using EmberDelivery.BuildingBlocks.Infrastructure.Identity;

using Microsoft.Extensions.DependencyInjection;

namespace EmberDelivery.BuildingBlocks.Infrastructure.Presentation;

public static class WebApiServiceCollectionExtensions
{
    public static IServiceCollection AddWebApiDefaults(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();
        services.AddOpenApi();

        return services;
    }
}
