using MassTransit;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace EmberDelivery.BuildingBlocks.Infrastructure.Messaging;

public static class MessageRetryExtensions
{
    // Retry must sit in front of the consumer factory and the outbox: every attempt then gets a new scope and DbContext
    // and re-reads the latest state instead of replaying a stale snapshot.
    public static void UseDefaultMessageRetry(
        this IConsumePipeConfigurator configurator,
        Action<IRetryConfigurator>? handleAdditional = null)
    {
        configurator.UseMessageRetry(retryCfg =>
        {
            retryCfg.Handle<DbUpdateConcurrencyException>();
            retryCfg.Handle<PostgresException>(ex => ex.IsTransient);
            retryCfg.Handle<DbUpdateException>(ex => ex.InnerException is PostgresException { IsTransient: true });

            handleAdditional?.Invoke(retryCfg);

            retryCfg.Exponential(
                retryLimit: 5,
                minInterval: TimeSpan.FromMilliseconds(50),
                maxInterval: TimeSpan.FromSeconds(2),
                intervalDelta: TimeSpan.FromMilliseconds(100));
        });
    }
}
