using EmberDelivery.BuildingBlocks.Infrastructure.Identity;
using EmberDelivery.BuildingBlocks.Infrastructure.Presentation;
using EmberDelivery.Ordering.Api.Extensions;
using EmberDelivery.Ordering.Application;
using EmberDelivery.Ordering.Infrastructure;
using EmberDelivery.Ordering.Infrastructure.Persistence;
using EmberDelivery.ServiceDefaults;

using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddWebApiServices();
builder.AddKeycloakAuth();

builder.Services.AddOrderingApplication();
builder.AddOrderingInfrastructure(typeof(Program).Assembly);
builder.Services.AddEndpoints(typeof(Program).Assembly);

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    using IServiceScope scope = app.Services.CreateScope();
    OrderingDbContext dbContext = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
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
