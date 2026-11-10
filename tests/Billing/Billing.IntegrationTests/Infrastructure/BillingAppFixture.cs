using System.Data.Common;

using EmberDelivery.Billing.Api.Infrastructure.PaymentGateway;
using EmberDelivery.Billing.Api.Infrastructure.Persistence;

using MassTransit;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Respawn;

using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

using Xunit;

using static EmberDelivery.Billing.IntegrationEvents.PaymentContracts;

namespace EmberDelivery.Billing.IntegrationTests.Infrastructure;

public sealed class BillingAppFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Kept well below the production default so the decline path is reachable with ordinary test amounts.
    public const decimal DeclineAmountAbove = 1_000m;

    private const string OutcomeObserverQueue = "billing-integration-tests-outcomes";

    private static readonly IConfigurationRoot Config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("infra.json", optional: false)
        .Build();

    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder(
            Config["Infrastructure:Postgres"] ?? throw new InvalidOperationException("Postgres image configuration is missing."))
        .WithDatabase("billing_db")
        .WithUsername("postgres")
        .WithPassword("IntegrationTestPass123!")
        .Build();

    private readonly RabbitMqContainer _rabbitMqContainer = new RabbitMqBuilder(
            Config["Infrastructure:RabbitMq"] ?? throw new InvalidOperationException("RabbitMq image configuration is missing."))
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    private DbConnection _dbConnection = null!;
    private Respawner _respawner = null!;
    private ConnectHandle? _consumeObserverHandle;
    private HostReceiveEndpointHandle? _outcomeObserverHandle;

    public string DbConnectionString => _dbContainer.GetConnectionString();

    public ControllablePaymentGateway Gateway => Services.GetRequiredService<ControllablePaymentGateway>();

    public SaveChangesFaultInjector FaultInjector => Services.GetRequiredService<SaveChangesFaultInjector>();

    public ConsumptionTracker Consumption { get; } = new();

    public PaymentOutcomeRecorder Outcomes { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_dbContainer.StartAsync(), _rabbitMqContainer.StartAsync());

        DbContextOptionsBuilder<BillingDbContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(DbConnectionString);

        await using (BillingDbContext dbContext = new(optionsBuilder.Options))
        {
            await dbContext.Database.MigrateAsync();
        }

        // Force host startup (and bus start, see WaitUntilStarted) only after the schema exists.
        _ = Server;

        IBus bus = Services.GetRequiredService<IBus>();
        _consumeObserverHandle = bus.ConnectConsumeObserver(Consumption);

        _outcomeObserverHandle = bus.ConnectReceiveEndpoint(OutcomeObserverQueue, endpoint =>
        {
            endpoint.Handler<FundsHeldIntegrationEvent>(context =>
            {
                Outcomes.Record(context.Message);
                return Task.CompletedTask;
            });

            endpoint.Handler<FundsHoldFailedIntegrationEvent>(context =>
            {
                Outcomes.Record(context.Message);
                return Task.CompletedTask;
            });
        });

        await _outcomeObserverHandle.Ready;

        _dbConnection = new NpgsqlConnection(DbConnectionString);
        await _dbConnection.OpenAsync();

        _respawner = await Respawner.CreateAsync(_dbConnection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = ["__EFMigrationsHistory"]
        });
    }

    public async Task ResetAsync()
    {
        Gateway.Reset();
        FaultInjector.Disarm();
        Consumption.Reset();
        Outcomes.Reset();

        const int maxAttempts = 5;

        for (int attempt = 1; attempt < maxAttempts; attempt++)
        {
            try
            {
                await _respawner.ResetAsync(_dbConnection);
                return;
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.DeadlockDetected)
            {
                // The outbox delivery service may still be draining the previous test's messages; its row locks can
                // deadlock with Respawn's bulk DELETE, and Postgres aborts one side, so retrying is safe.
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt));
            }
        }

        await _respawner.ResetAsync(_dbConnection);
    }

    public override async ValueTask DisposeAsync()
    {
        _consumeObserverHandle?.Disconnect();

        if (_outcomeObserverHandle is not null)
        {
            await _outcomeObserverHandle.StopAsync();
        }

        await base.DisposeAsync();

        if (_dbConnection is not null)
        {
            await _dbConnection.DisposeAsync();
        }

        await Task.WhenAll(_dbContainer.DisposeAsync().AsTask(), _rabbitMqContainer.DisposeAsync().AsTask());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        builder.UseSetting("Logging:LogLevel:Microsoft", "Warning");
        builder.UseSetting("Logging:LogLevel:MassTransit", "Warning");

        builder.UseSetting("ConnectionStrings:billing-db", DbConnectionString);
        builder.UseSetting("ConnectionStrings:messaging", _rabbitMqContainer.GetConnectionString());
        builder.UseSetting($"{FakePaymentGatewayOptions.SectionName}:DeclineAmountAbove", DeclineAmountAbove.ToString(System.Globalization.CultureInfo.InvariantCulture));

        builder.ConfigureTestServices(services =>
        {
            // Tests publish right after host start; queues must be bound before the first message or it is lost.
            services.Configure<MassTransitHostOptions>(options =>
            {
                options.WaitUntilStarted = true;
                options.StartTimeout = TimeSpan.FromMinutes(1);
            });

            DecoratePaymentGateway(services);

            services.AddSingleton<SaveChangesFaultInjector>();
            services.ConfigureDbContext<BillingDbContext>((serviceProvider, options) =>
                options.AddInterceptors(serviceProvider.GetRequiredService<SaveChangesFaultInjector>()));

            services.AddAuthentication(defaultScheme: TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            services.Configure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultScheme = TestAuthHandler.SchemeName;
            });
        });
    }

    // The production gateway implementation is internal, so it is re-registered by its runtime type and wrapped;
    // its singleton provider-side idempotency cache is preserved, which the crash-recovery tests rely on.
    private static void DecoratePaymentGateway(IServiceCollection services)
    {
        ServiceDescriptor original = services.Single(descriptor => descriptor.ServiceType == typeof(IPaymentGateway));
        Type implementationType = original.ImplementationType
            ?? throw new InvalidOperationException("IPaymentGateway is expected to be registered by implementation type.");

        services.Remove(original);
        services.AddSingleton(implementationType);
        services.AddSingleton(serviceProvider =>
            new ControllablePaymentGateway((IPaymentGateway)serviceProvider.GetRequiredService(implementationType)));
        services.AddSingleton<IPaymentGateway>(serviceProvider =>
            serviceProvider.GetRequiredService<ControllablePaymentGateway>());
    }
}

[CollectionDefinition("BillingIntegration")]
public sealed class BillingIntegrationTestCollection : ICollectionFixture<BillingAppFixture>
{
}
