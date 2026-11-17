using EmberDelivery.AppHost.Utils;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

var images = InfrastructureImages.LoadFromOutputDirectory();

IResourceBuilder<PostgresServerResource> postgres = builder.AddPostgres("postgres")
    .WithPinnedImage(images.Postgres)
    .WithDataVolume();

IResourceBuilder<PostgresDatabaseResource> orderingDb = postgres.AddDatabase("ordering-db", databaseName: "ordering_db");
IResourceBuilder<PostgresDatabaseResource> billingDb = postgres.AddDatabase("billing-db", databaseName: "billing_db");

IResourceBuilder<RedisResource> redis = builder.AddRedis("redis")
    .WithPinnedImage(images.Redis);

// Built from infra/rabbitmq on top of the infra.json image to add the delayed-message exchange required by the saga
// timeouts. The base image already ships the management plugin, so the UI port is exposed directly;
// WithManagementPlugin() would rewrite the image tag and override the pinned version.
IResourceBuilder<RabbitMQServerResource> messaging = builder.AddRabbitMQ("messaging")
    .WithDockerfile("../../../infra/rabbitmq")
    .WithBuildArg("RABBITMQ_IMAGE", images.RabbitMq.ToString())
    .WithHttpEndpoint(targetPort: 15672, name: "management");

// Fixed host port: services validate tokens against issuers on localhost:8080 (KeycloakAuthOptions defaults).
IResourceBuilder<KeycloakResource> keycloak = builder.AddKeycloak("keycloak", port: 8080)
    .WithPinnedImage(images.Keycloak)
    .WithDataVolume();

builder.AddProject<Projects.Ordering_Api>("ordering-api")
    .WithReference(orderingDb)
    .WithReference(redis)
    .WithReference(messaging)
    .WithReference(keycloak)
    .WaitFor(orderingDb)
    .WaitFor(redis)
    .WaitFor(messaging)
    .WaitFor(keycloak);

builder.AddProject<Projects.Billing_Api>("billing-api")
    .WithReference(billingDb)
    .WithReference(messaging)
    .WithReference(keycloak)
    .WaitFor(billingDb)
    .WaitFor(messaging)
    .WaitFor(keycloak);

builder.AddProject<Projects.Kitchen_Api>("kitchen-api");

await builder.Build().RunAsync();
