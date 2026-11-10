using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EmberDelivery.Billing.Api.Infrastructure.Persistence;

internal sealed class BillingDbContextFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    public BillingDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<BillingDbContext> optionsBuilder = new();

        // Read connection string from standard environment variables if running against a real container,
        // otherwise fall back to a credential-free dummy string for schema generation.
        string connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__billing-db")
            ?? Environment.GetEnvironmentVariable("BILLING_DB_CONNECTION")
            ?? "Host=localhost;Database=billing_db";

        optionsBuilder.UseNpgsql(connectionString);

        return new BillingDbContext(optionsBuilder.Options);
    }
}
