using EmberDelivery.Ordering.Application.Sagas.OrderFulfillment;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Npgsql;

namespace EmberDelivery.Ordering.IntegrationTests.Infrastructure;

// A real race between two writers is non-deterministic; bumping the version from a separate connection
// right before the writer commits reproduces the exact interleaving "someone else wrote after I read".
public sealed class ConcurrentWriteSimulator(string connectionString) : SaveChangesInterceptor
{
    private const string BumpOrderVersionSql = """UPDATE orders SET "Version" = "Version" + 1 WHERE "Id" = @id""";

    private const string BumpSagaVersionSql =
        """UPDATE order_saga_states SET "Version" = "Version" + 1 WHERE "CorrelationId" = @id""";

    private readonly Lock _gate = new();
    private ArmedWrite? _armedWrite;
    private int _injectedConflicts;

    public int InjectedConflicts => Volatile.Read(ref _injectedConflicts);

    public void ArmForNextSaveOf(Guid orderId) =>
        Arm(new ArmedWrite(orderId, BumpOrderVersionSql, IsOrderBeingUpdated));

    public void ArmForNextSagaSaveOf(Guid correlationId) =>
        Arm(new ArmedWrite(correlationId, BumpSagaVersionSql, IsSagaBeingUpdated));

    public void Disarm()
    {
        lock (_gate)
        {
            _armedWrite = null;
            _injectedConflicts = 0;
        }
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArmedWrite? armedWrite = TakeArmedWriteIfTargeted(eventData.Context);

        if (armedWrite is not null)
        {
            await using NpgsqlConnection connection = new(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using NpgsqlCommand command = new(armedWrite.BumpVersionSql, connection);
            command.Parameters.AddWithValue("id", armedWrite.EntityId);
            await command.ExecuteNonQueryAsync(cancellationToken);

            Interlocked.Increment(ref _injectedConflicts);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static bool IsOrderBeingUpdated(DbContext context, Guid orderId) =>
        context.ChangeTracker
            .Entries<Order>()
            .Any(entry => entry.State == EntityState.Modified && entry.Entity.Id == orderId);

    private static bool IsSagaBeingUpdated(DbContext context, Guid correlationId) =>
        context.ChangeTracker
            .Entries<OrderSagaState>()
            .Any(entry => entry.State == EntityState.Modified && entry.Entity.CorrelationId == correlationId);

    private void Arm(ArmedWrite armedWrite)
    {
        lock (_gate)
        {
            _armedWrite = armedWrite;
        }
    }

    private ArmedWrite? TakeArmedWriteIfTargeted(DbContext? context)
    {
        if (context is null)
        {
            return null;
        }

        lock (_gate)
        {
            if (_armedWrite is null || !_armedWrite.IsTargetedBy(context, _armedWrite.EntityId))
            {
                return null;
            }

            ArmedWrite armedWrite = _armedWrite;
            _armedWrite = null;
            return armedWrite;
        }
    }

    private sealed record ArmedWrite(Guid EntityId, string BumpVersionSql, Func<DbContext, Guid, bool> IsTargetedBy);
}
