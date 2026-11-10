using EmberDelivery.Billing.Api.Extensions;
using EmberDelivery.Billing.Api.Infrastructure;
using EmberDelivery.Billing.Api.Infrastructure.Persistence;
using EmberDelivery.BuildingBlocks.Infrastructure.Identity;
using EmberDelivery.BuildingBlocks.Infrastructure.Presentation;
using EmberDelivery.ServiceDefaults;

using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddWebApiServices();
builder.AddKeycloakAuth(authorization => authorization.AddPolicy(
    BillingPolicies.CustomerOrAdmin,
    policy => policy.RequireRole(Roles.Customer, Roles.Admin)));

builder.AddBillingInfrastructure();
builder.Services.AddEndpoints(typeof(Program).Assembly);

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    using IServiceScope scope = app.Services.CreateScope();
    BillingDbContext dbContext = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
    await dbContext.Database.MigrateAsync();
}

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
