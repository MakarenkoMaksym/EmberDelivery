using System.Reflection;

using EmberDelivery.BuildingBlocks.Application.Abstractions;
using EmberDelivery.BuildingBlocks.Application.Messaging;
using EmberDelivery.BuildingBlocks.Infrastructure.Messaging;
using EmberDelivery.BuildingBlocks.Infrastructure.Persistence;
using EmberDelivery.Ordering.Application.Abstractions;
using EmberDelivery.Ordering.Application.Sagas.OrderFulfillment;
using EmberDelivery.Ordering.Domain.Aggregates.Coupons;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;
using EmberDelivery.Ordering.Infrastructure.Persistence;
using EmberDelivery.Ordering.Infrastructure.Persistence.Projections;
using EmberDelivery.Ordering.Infrastructure.Persistence.ReadServices;
using EmberDelivery.Ordering.Infrastructure.Persistence.Repositories;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

namespace EmberDelivery.Ordering.Infrastructure;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddOrderingInfrastructure(
        this IHostApplicationBuilder builder,
        params Assembly[] consumerAssemblies)
    {
        builder.AddNpgsqlDbContext<OrderingDbContext>("ordering-db");
        builder.AddRedisClient("redis");

        builder.Services.AddScoped<IUnitOfWork, UnitOfWork<OrderingDbContext>>();
        builder.Services.AddScoped<IOrderRepository, OrderRepository>();
        builder.Services.AddScoped<ICouponRepository, CouponRepository>();
        builder.Services.AddScoped<IOrderReadService, OrderReadService>();
        builder.Services.AddScoped<IRestaurantMenuSnapshotReader, RestaurantMenuSnapshotReader>();
        builder.Services.AddScoped<IEventBus, MassTransitEventBus>();

        builder.Services.AddMassTransit(busConfigurator =>
        {
            busConfigurator.SetKebabCaseEndpointNameFormatter();

            if (consumerAssemblies.Length > 0)
            {
                busConfigurator.AddConsumers(consumerAssemblies);
            }

            busConfigurator.AddConsumers(typeof(DependencyInjection).Assembly);

            busConfigurator.AddSagaStateMachine<OrderFulfillmentStateMachine, OrderSagaState>()
                .EntityFrameworkRepository(r =>
                {
                    r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                    r.ExistingDbContext<OrderingDbContext>();
                    r.UsePostgres();
                });

            busConfigurator.AddEntityFrameworkOutbox<OrderingDbContext>(outbox =>
            {
                outbox.QueryDelay = TimeSpan.FromSeconds(3);
                outbox.UsePostgres();
                outbox.UseBusOutbox();
            });

            busConfigurator.AddConfigureEndpointsCallback((context, _, cfg) =>
            {
                // Retry must wrap the outbox: every attempt then gets a new scope and DbContext, re-reads the saga or
                // aggregate at its latest version and re-applies the transition instead of replaying a stale snapshot.
                cfg.UseMessageRetry(retryCfg =>
                {
                    retryCfg.Handle<DbUpdateConcurrencyException>();
                    retryCfg.Handle<PostgresException>(ex => ex.IsTransient);
                    retryCfg.Handle<DbUpdateException>(ex => ex.InnerException is PostgresException { IsTransient: true });
                    retryCfg.Exponential(
                        retryLimit: 5,
                        minInterval: TimeSpan.FromMilliseconds(50),
                        maxInterval: TimeSpan.FromSeconds(2),
                        intervalDelta: TimeSpan.FromMilliseconds(100));
                });

                cfg.UseEntityFrameworkOutbox<OrderingDbContext>(context);
            });

            busConfigurator.UsingRabbitMq((context, cfg) =>
            {
                string? rabbitConnectionString = builder.Configuration.GetConnectionString("messaging");
                if (!string.IsNullOrWhiteSpace(rabbitConnectionString))
                {
                    cfg.Host(new Uri(rabbitConnectionString));
                }

                cfg.UseDelayedMessageScheduler();
                cfg.ConfigureEndpoints(context);
            });
        });

        return builder;
    }
}
