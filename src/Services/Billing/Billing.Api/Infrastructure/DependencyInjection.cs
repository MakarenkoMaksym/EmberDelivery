using EmberDelivery.Billing.Api.Infrastructure.Messaging;
using EmberDelivery.Billing.Api.Infrastructure.PaymentGateway;
using EmberDelivery.Billing.Api.Infrastructure.Persistence;

using MassTransit;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace EmberDelivery.Billing.Api.Infrastructure;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddBillingInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddNpgsqlDbContext<BillingDbContext>("billing-db");

        builder.Services.Configure<FakePaymentGatewayOptions>(
            builder.Configuration.GetSection(FakePaymentGatewayOptions.SectionName));

        builder.Services.AddSingleton<OutboxUnitOfWorkFactory>();

        // Singleton so the emulated provider-side idempotency cache outlives individual message scopes.
        builder.Services.AddSingleton<IPaymentGateway, FakePaymentGateway>();

        builder.Services.AddMassTransit(busConfigurator =>
        {
            busConfigurator.SetKebabCaseEndpointNameFormatter();

            busConfigurator.AddConsumers(typeof(DependencyInjection).Assembly);

            // Bus outbox only, deliberately without the consumer outbox/inbox: the inbox would open a transaction on
            // InboxState before Consume and keep it open across the payment gateway call. Consumers instead call the
            // gateway first and then commit the hold together with the outbox rows in one short SaveChanges (events go
            // through OutboxUnitOfWork, see there why not IPublishEndpoint), while duplicate deliveries are absorbed by
            // business keys (one hold per order, unique ledger and provider idempotency keys) rather than by MessageId.
            busConfigurator.AddEntityFrameworkOutbox<BillingDbContext>(outbox =>
            {
                outbox.QueryDelay = TimeSpan.FromSeconds(3);
                outbox.UsePostgres();
                outbox.UseBusOutbox();
            });

            busConfigurator.AddConfigureEndpointsCallback((_, _, cfg) =>
            {
                // Endpoint-level retry sits in front of the consumer factory, so each attempt gets a fresh scope and
                // DbContext, re-reads the hold and re-calls the gateway with the same idempotency key. Unique violations
                // are retried because the loser of a duplicate race then finds the winner's committed hold or ledger
                // entry and turns into a no-op.
                cfg.UseMessageRetry(retryCfg =>
                {
                    retryCfg.Handle<DbUpdateConcurrencyException>();
                    retryCfg.Handle<PaymentGatewayUnavailableException>();
                    retryCfg.Handle<PostgresException>(ex => ex.IsTransient);
                    retryCfg.Handle<DbUpdateException>(ex => ex.InnerException is PostgresException
                    {
                        IsTransient: true
                    } or PostgresException
                    {
                        SqlState: PostgresErrorCodes.UniqueViolation
                    });
                    retryCfg.Exponential(
                        retryLimit: 5,
                        minInterval: TimeSpan.FromMilliseconds(50),
                        maxInterval: TimeSpan.FromSeconds(2),
                        intervalDelta: TimeSpan.FromMilliseconds(100));
                });
            });

            busConfigurator.UsingRabbitMq((context, cfg) =>
            {
                string? rabbitConnectionString = builder.Configuration.GetConnectionString("messaging");
                if (!string.IsNullOrWhiteSpace(rabbitConnectionString))
                {
                    cfg.Host(new Uri(rabbitConnectionString));
                }

                cfg.ConfigureEndpoints(context);
            });
        });

        return builder;
    }
}
