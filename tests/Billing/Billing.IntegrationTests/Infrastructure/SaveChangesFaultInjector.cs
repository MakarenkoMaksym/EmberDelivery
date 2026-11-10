using EmberDelivery.Billing.Api.Domain.PaymentHolds;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Npgsql;

namespace EmberDelivery.Billing.IntegrationTests.Infrastructure;

// Reproduces "the gateway already said yes, then the commit was lost" (connection drop, failover) without killing the
// process: the consumer's SaveChanges fails once with a transient Postgres error, exactly what the retry policy handles.
public sealed class SaveChangesFaultInjector : SaveChangesInterceptor
{
    private const string SerializationFailureSqlState = "40001";

    private int _remainingFailures;

    public void FailNextPaymentHoldSave() => Interlocked.Exchange(ref _remainingFailures, 1);

    public void Disarm() => Interlocked.Exchange(ref _remainingFailures, 0);

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (IsPaymentHoldWrite(eventData.Context) && TryConsumeFailure())
        {
            throw new DbUpdateException(
                "Simulated lost commit after a successful gateway call.",
                new PostgresException("Simulated serialization failure.", "ERROR", "ERROR", SerializationFailureSqlState));
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static bool IsPaymentHoldWrite(DbContext? context) =>
        context is not null
        && context.ChangeTracker
            .Entries<PaymentHold>()
            .Any(entry => entry.State is EntityState.Added or EntityState.Modified);

    private bool TryConsumeFailure()
    {
        while (true)
        {
            int remaining = Volatile.Read(ref _remainingFailures);
            if (remaining <= 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _remainingFailures, remaining - 1, remaining) == remaining)
            {
                return true;
            }
        }
    }
}
