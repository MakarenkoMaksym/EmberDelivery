using System.Data.Common;

using EmberDelivery.Ordering.Infrastructure.Persistence;

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
using Testcontainers.Redis;

using Xunit;

namespace EmberDelivery.Ordering.IntegrationTests.Infrastructure;

public sealed class OrderingAppFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly IConfigurationRoot Config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("infra.json", optional: false)
        .Build();

    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder(
            Config["Infrastructure:Postgres"] ?? throw new InvalidOperationException("Postgres image configuration is missing."))
        .WithDatabase("ordering_db")
        .WithUsername("postgres")
        .WithPassword("IntegrationTestPass123!")
        .Build();

    private readonly RedisContainer _redisContainer = new RedisBuilder(
            Config["Infrastructure:Redis"] ?? throw new InvalidOperationException("Redis image configuration is missing."))
        .Build();

    private readonly RabbitMqContainer _rabbitMqContainer = new RabbitMqBuilder(
            Config["Infrastructure:RabbitMq"] ?? throw new InvalidOperationException("RabbitMq image configuration is missing."))
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    private DbConnection _dbConnection = null!;
    private Respawner _respawner = null!;

    public string DbConnectionString => _dbContainer.GetConnectionString();

    public string RedisConnectionString => _redisContainer.GetConnectionString();

    public string RabbitMqConnectionString => _rabbitMqContainer.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(
            _dbContainer.StartAsync(),
            _redisContainer.StartAsync(),
            _rabbitMqContainer.StartAsync());

        DbContextOptionsBuilder<OrderingDbContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(DbConnectionString);

        await using (OrderingDbContext dbContext = new(optionsBuilder.Options))
        {
            await dbContext.Database.MigrateAsync();
        }

        // Force WebApplication host startup after migrations are applied.
        _ = Server;

        _dbConnection = new NpgsqlConnection(DbConnectionString);
        await _dbConnection.OpenAsync();

        _respawner = await Respawner.CreateAsync(_dbConnection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = ["__EFMigrationsHistory"]
        });
    }

    public async Task ResetDatabaseAsync()
    {
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
                // Messages from the previous test may still be consumed in the background (outbox -> saga), and their
                // row locks can deadlock with Respawn's bulk DELETE. Postgres aborts one side, so retrying is safe.
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt));
            }
        }

        await _respawner.ResetAsync(_dbConnection);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        if (_dbConnection is not null)
        {
            await _dbConnection.DisposeAsync();
        }

        await Task.WhenAll(
            _dbContainer.DisposeAsync().AsTask(),
            _redisContainer.DisposeAsync().AsTask(),
            _rabbitMqContainer.DisposeAsync().AsTask());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        builder.UseSetting("Logging:LogLevel:Microsoft", "Warning");
        builder.UseSetting("Logging:LogLevel:MassTransit", "Warning");

        builder.UseSetting("ConnectionStrings:ordering-db", DbConnectionString);
        builder.UseSetting("ConnectionStrings:redis", RedisConnectionString);
        builder.UseSetting("ConnectionStrings:messaging", RabbitMqConnectionString);

        builder.ConfigureTestServices(services =>
        {
            // Consumer tests publish right after host start; queues must be bound before the first message or it is lost.
            services.Configure<MassTransitHostOptions>(options =>
            {
                options.WaitUntilStarted = true;
                options.StartTimeout = TimeSpan.FromMinutes(1);
            });

            services.AddSingleton(new ConcurrentWriteSimulator(DbConnectionString));
            services.ConfigureDbContext<OrderingDbContext>((serviceProvider, options) =>
                options.AddInterceptors(serviceProvider.GetRequiredService<ConcurrentWriteSimulator>()));

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
}

[CollectionDefinition("OrderingIntegration")]
public sealed class OrderingIntegrationTestCollection : ICollectionFixture<OrderingAppFixture>
{
}
