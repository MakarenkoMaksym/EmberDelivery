using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EmberDelivery.BuildingBlocks.Infrastructure.Persistence;

public static class MigrationExtensions
{
    // Outside Development the schema is owned by the deployment pipeline, so concurrent replicas never race on migrations.
    public static async Task ApplyMigrationsInDevelopmentAsync<TDbContext>(this WebApplication app)
        where TDbContext : DbContext
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        TDbContext dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        await dbContext.Database.MigrateAsync();
    }
}
