using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EmberDelivery.Ordering.Infrastructure.Persistence;

internal sealed class OrderingDbContextFactory : IDesignTimeDbContextFactory<OrderingDbContext>
{
    public OrderingDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<OrderingDbContext> optionsBuilder = new();

        // Read connection string from standard environment variables if running against a real container,
        // otherwise fall back to a credential-free dummy string for schema generation.
        string connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__ordering-db")
            ?? Environment.GetEnvironmentVariable("ORDERING_DB_CONNECTION")
            ?? "Host=localhost;Database=ordering_db";

        optionsBuilder.UseNpgsql(connectionString);

        return new OrderingDbContext(optionsBuilder.Options);
    }
}
