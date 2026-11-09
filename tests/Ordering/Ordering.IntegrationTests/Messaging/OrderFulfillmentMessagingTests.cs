using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;

using EmberDelivery.Ordering.Application.Sagas.OrderFulfillment;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;
using EmberDelivery.Ordering.IntegrationEvents;
using EmberDelivery.Ordering.IntegrationTests.Infrastructure;

using FluentAssertions;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

using static EmberDelivery.Billing.IntegrationEvents.PaymentContracts;
using static EmberDelivery.Ordering.Api.Endpoints.Orders.CancelOrderEndpoint;

namespace EmberDelivery.Ordering.IntegrationTests.Messaging;

// Unit tests cover the state machine in memory; these prove the wiring around it:
// transactional outbox -> RabbitMQ -> EF-persisted saga -> lifecycle event -> Order consumer.
public sealed class OrderFulfillmentMessagingTests(OrderingAppFixture fixture) : BaseIntegrationTest(fixture)
{
    [Fact]
    public async Task CreateOrder_ShouldPublishOrderSubmittedThroughOutboxAndStartFulfillmentSaga()
    {
        // Act
        Guid orderId = await CreateOrderAsync(await SeedRestaurantMenuAsync());

        // Assert
        await WaitForSagaStateAsync(orderId, "AwaitingPaymentHold");

        OrderSagaState saga = await GetSagaAsync(orderId);
        saga.CustomerId.Should().Be(CustomerId);
        saga.TotalAmount.Should().Be(400m);
        saga.Currency.Should().Be("UAH");
    }

    [Fact]
    public async Task FundsHeld_ShouldDriveSagaToKitchenConfirmationAndMarkOrderAsPaid()
    {
        // Arrange
        Guid orderId = await CreateOrderAsync(await SeedRestaurantMenuAsync());
        await WaitForSagaStateAsync(orderId, "AwaitingPaymentHold");
        var paymentHoldId = Guid.NewGuid();

        // Act
        await PublishAndWaitForConsumptionAsync(new FundsHeldIntegrationEvent(orderId, paymentHoldId));

        // Assert
        await WaitUntilAsync(
            db => db.Orders.AnyAsync(x => x.Id == orderId && x.Status == OrderStatus.Paid, CancellationToken),
            $"order {orderId} to become Paid");

        OrderSagaState saga = await GetSagaAsync(orderId);
        saga.CurrentState.Should().Be("AwaitingKitchenConfirmation");
        saga.PaymentHoldId.Should().Be(paymentHoldId);
    }

    [Fact]
    public async Task CancelOrderByCustomer_ShouldNotifySagaThroughOutboxAndCancelIt()
    {
        // Arrange
        Guid orderId = await CreateOrderAsync(await SeedRestaurantMenuAsync());
        await WaitForSagaStateAsync(orderId, "AwaitingPaymentHold");

        // Act
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            $"api/v1/orders/{orderId}/cancel",
            new CancelOrderRequest("Ordered by mistake"),
            CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await WaitForSagaStateAsync(orderId, "Cancelled");
        (await GetSagaAsync(orderId)).FailureReason.Should().Be("Ordered by mistake");
    }

    [Fact]
    public async Task FundsHeldAndCustomerCancellation_WhenRacingOnSameSaga_ShouldRetryLoserAndReleaseHoldExactlyOnce()
    {
        // Arrange
        Guid orderId = await CreateOrderAsync(await SeedRestaurantMenuAsync());
        await WaitForSagaStateAsync(orderId, "AwaitingPaymentHold");
        var paymentHoldId = Guid.NewGuid();
        const string cancellationReason = "Changed my mind";

        ConcurrentQueue<ReleaseFundsHoldCommand> releaseCommands = new();
        TaskCompletionSource firstReleaseReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Billing does not exist yet, so a temporary queue stands in for it and records the compensation command.
        HostReceiveEndpointHandle billingStub = Fixture.Services.GetRequiredService<IBus>().ConnectReceiveEndpoint(
            new TemporaryEndpointDefinition(),
            configureEndpoint: cfg => cfg.Handler<ReleaseFundsHoldCommand>(context =>
            {
                releaseCommands.Enqueue(context.Message);
                firstReleaseReceived.TrySetResult();
                return Task.CompletedTask;
            }));
        await billingStub.Ready;

        // Whichever message commits to the saga row first is guaranteed to hit a stale version, so the retry path
        // is exercised on every run instead of only when the broker happens to interleave the two deliveries.
        ConcurrentWriteSimulator concurrentWriter = Fixture.Services.GetRequiredService<ConcurrentWriteSimulator>();
        concurrentWriter.ArmForNextSagaSaveOf(orderId);

        try
        {
            // Act
            await Task.WhenAll(
                PublishAndWaitForConsumptionAsync(new FundsHeldIntegrationEvent(orderId, paymentHoldId)),
                PublishAndWaitForConsumptionAsync(new OrderCancelledByCustomerIntegrationEvent(orderId, cancellationReason)));

            // Assert
            // Both messages were consumed (none landed in _error) despite the optimistic concurrency conflict.
            concurrentWriter.InjectedConflicts.Should().Be(1);

            OrderSagaState saga = await GetSagaAsync(orderId);
            saga.CurrentState.Should().Be("Cancelled");
            saga.PaymentHoldId.Should().Be(paymentHoldId);
            saga.FailureReason.Should().Be(cancellationReason);

            await firstReleaseReceived.Task.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken);
            releaseCommands.Should().ContainSingle()
                .Which.Should().Be(new ReleaseFundsHoldCommand(orderId, paymentHoldId, cancellationReason));
        }
        finally
        {
            await billingStub.StopAsync(CancellationToken);
        }
    }

    private Task WaitForSagaStateAsync(Guid orderId, string expectedState) =>
        WaitUntilAsync(
            db => db.OrderSagaStates.AnyAsync(
                x => x.CorrelationId == orderId && x.CurrentState == expectedState,
                CancellationToken),
            $"saga {orderId} to reach state {expectedState}");

    private Task<OrderSagaState> GetSagaAsync(Guid orderId) =>
        QueryDbAsync(db => db.OrderSagaStates
            .AsNoTracking()
            .SingleAsync(x => x.CorrelationId == orderId, CancellationToken));
}
