using System.Net.Http.Headers;

using EmberDelivery.Billing.Api.Domain.PaymentHolds;
using EmberDelivery.Billing.Api.Infrastructure.Persistence;

using FluentAssertions;

using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Xunit;
using Xunit.Sdk;
using Xunit.v3;

using static EmberDelivery.Billing.IntegrationEvents.PaymentContracts;

[assembly: Parallelization(Mode = ParallelMode.None)]

namespace EmberDelivery.Billing.IntegrationTests.Infrastructure;

[Collection("BillingIntegration")]
public abstract class BaseIntegrationTest(BillingAppFixture fixture) : IAsyncLifetime
{
    protected const string Currency = "UAH";
    protected const decimal StandardAmount = 250.00m;

    private static readonly TimeSpan EventualConsistencyTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(100);

    // Covers the hop from the outbox delivery service through the broker to the observer queue once the outbox is empty.
    private static readonly TimeSpan BrokerSettleTime = TimeSpan.FromSeconds(1);

    protected BillingAppFixture Fixture { get; } = fixture;

    protected ControllablePaymentGateway Gateway => Fixture.Gateway;

    protected PaymentOutcomeRecorder Outcomes => Fixture.Outcomes;

    protected static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public virtual async ValueTask InitializeAsync() => await Fixture.ResetAsync();

    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    protected static string AuthorizeKey(Guid orderId) => PaymentHold.IdempotencyKeyFor(orderId, LedgerEntryType.Authorization);

    protected static string CaptureKey(Guid orderId) => PaymentHold.IdempotencyKeyFor(orderId, LedgerEntryType.Capture);

    protected static string ReleaseKey(Guid orderId) => PaymentHold.IdempotencyKeyFor(orderId, LedgerEntryType.Release);

    protected async Task<Guid> PublishAsync<T>(T message)
        where T : class
    {
        Guid messageId = NewId.NextGuid();

        IBus bus = Fixture.Services.GetRequiredService<IBus>();
        await bus.Publish(message, context => context.MessageId = messageId, CancellationToken);

        return messageId;
    }

    protected async Task<ConsumptionOutcome> WaitForConsumptionAsync(Guid messageId)
    {
        ConsumptionOutcome? outcome = null;

        await WaitUntilAsync(
            () => Task.FromResult(Fixture.Consumption.TryGetOutcome(messageId, out outcome)),
            $"message {messageId} to be consumed");

        return outcome!;
    }

    protected async Task PublishAndWaitForSuccessAsync<T>(T message)
        where T : class
    {
        ConsumptionOutcome outcome = await WaitForConsumptionAsync(await PublishAsync(message));
        outcome.Succeeded.Should().BeTrue(outcome.Fault?.ToString());
    }

    // Drives the real HoldFunds flow and returns the hold id the saga would receive, so later stages start from a hold
    // that was created exactly like in production rather than from hand-seeded rows.
    protected async Task<Guid> HoldFundsAsync(Guid orderId, Guid customerId, decimal amount = StandardAmount)
    {
        await PublishAsync(new HoldFundsCommand(orderId, customerId, amount, Currency));

        await WaitUntilAsync(
            () => Task.FromResult(Outcomes.FundsHeldFor(orderId).Count > 0),
            $"FundsHeld for order {orderId}");

        return Outcomes.FundsHeldFor(orderId)[0].PaymentHoldId;
    }

    protected Task<T> QueryDbAsync<T>(Func<BillingDbContext, Task<T>> query) =>
        ExecuteScopeAsync(serviceProvider => query(serviceProvider.GetRequiredService<BillingDbContext>()));

    protected async Task<T> ExecuteScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using AsyncServiceScope scope = Fixture.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    protected Task<PaymentHold?> FindHoldByOrderAsync(Guid orderId) =>
        QueryDbAsync(db => db.PaymentHolds
            .AsNoTracking()
            .Include(x => x.LedgerEntries)
            .SingleOrDefaultAsync(x => x.OrderId == orderId, CancellationToken));

    protected Task<int> CountHoldsAsync(Guid orderId) =>
        QueryDbAsync(db => db.PaymentHolds.CountAsync(x => x.OrderId == orderId, CancellationToken));

    protected async Task WaitForHoldStatusAsync(Guid orderId, PaymentHoldStatus status) =>
        await WaitUntilAsync(
            () => QueryDbAsync(db => db.PaymentHolds.AnyAsync(x => x.OrderId == orderId && x.Status == status, CancellationToken)),
            $"payment hold of order {orderId} to become {status}");

    // "Exactly one event" can only be asserted once nothing else can still arrive: every outbox row has been handed
    // to the broker, and the broker has had time to deliver it to the observer queue.
    protected async Task WaitForOutboxDrainedAsync()
    {
        await WaitUntilAsync(
            async () => !await QueryDbAsync(db => db.Set<OutboxMessage>().AnyAsync(CancellationToken)),
            "the bus outbox to be drained");

        await Task.Delay(BrokerSettleTime, CancellationToken);
    }

    protected async Task<long> CountIdleInTransactionSessionsAsync()
    {
        await using NpgsqlConnection connection = new(Fixture.DbConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using NpgsqlCommand command = new(
            """
            SELECT count(*)
            FROM pg_stat_activity
            WHERE datname = current_database()
              AND pid <> pg_backend_pid()
              AND state IN ('idle in transaction', 'idle in transaction (aborted)')
            """,
            connection);

        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    protected HttpClient CreateClientFor(Guid userId, params string[] roles)
    {
        HttpClient client = Fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthHandler.SchemeName, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeaderName, string.Join(',', roles));
        return client;
    }

    protected static async Task WaitUntilAsync(Func<Task<bool>> condition, string awaitedOutcome)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        timeout.CancelAfter(EventualConsistencyTimeout);

        while (!timeout.IsCancellationRequested)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(PollingInterval, CancellationToken);
        }

        throw new TimeoutException($"Timed out after {EventualConsistencyTimeout.TotalSeconds}s waiting for {awaitedOutcome}.");
    }
}
