using EmberDelivery.BuildingBlocks.Infrastructure.Identity;
using EmberDelivery.BuildingBlocks.Infrastructure.Persistence;
using EmberDelivery.BuildingBlocks.Infrastructure.Presentation;
using EmberDelivery.Ordering.Application;
using EmberDelivery.Ordering.Infrastructure;
using EmberDelivery.Ordering.Infrastructure.Persistence;
using EmberDelivery.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddWebApiDefaults();
builder.AddKeycloakAuth();

builder.Services.AddOrderingApplication();
builder.AddOrderingInfrastructure(typeof(Program).Assembly);
builder.Services.AddEndpoints(typeof(Program).Assembly);

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.ApplyMigrationsInDevelopmentAsync<OrderingDbContext>();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

RouteGroupBuilder apiGroup = app.MapGroup("api/v1");
app.MapEndpoints(apiGroup);

await app.RunAsync();

public partial class Program
{
    protected Program()
    {
    }
}
