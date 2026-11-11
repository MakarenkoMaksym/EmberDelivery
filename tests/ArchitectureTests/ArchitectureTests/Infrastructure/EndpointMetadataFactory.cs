using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EmberDelivery.ArchitectureTests.Infrastructure;

internal interface IRouteEndpointSource : IDisposable
{
    IReadOnlyList<RouteEndpoint> CaptureRouteEndpoints();
}

// Generic over the entry point because every API declares its own global "Program", so the type cannot be named here.
internal sealed class EndpointMetadataFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>, IRouteEndpointSource
    where TEntryPoint : class
{
    // Aspire client integrations reject missing connection strings at registration, yet nothing connects until used.
    private const string PlaceholderPostgres = "Host=localhost;Database=placeholder;Username=placeholder;Password=placeholder";

    public IReadOnlyList<RouteEndpoint> CaptureRouteEndpoints() =>
        Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Anything other than Development skips startup migrations and the OpenAPI/health endpoints.
        builder.UseEnvironment("Testing");

        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        builder.UseSetting("ConnectionStrings:ordering-db", PlaceholderPostgres);
        builder.UseSetting("ConnectionStrings:billing-db", PlaceholderPostgres);
        builder.UseSetting("ConnectionStrings:redis", "localhost:6379");
        builder.UseSetting("ConnectionStrings:messaging", "amqp://guest:guest@localhost:5672");

        builder.ConfigureTestServices(services =>
        {
            // The bus and outbox workers would keep retrying RabbitMQ/Postgres in the background for no benefit here.
            ServiceDescriptor[] messagingWorkers = services
                .Where(descriptor => descriptor.ServiceType == typeof(IHostedService) && !descriptor.IsKeyedService)
                .Where(descriptor => descriptor.ImplementationType?.Namespace?.StartsWith("MassTransit", StringComparison.Ordinal) ?? false)
                .ToArray();

            foreach (ServiceDescriptor descriptor in messagingWorkers)
            {
                services.Remove(descriptor);
            }
        });
    }
}
