using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using EmberDelivery.BuildingBlocks.Application.Abstractions;
using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.Ordering.Domain.Aggregates.Coupons;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;
using EmberDelivery.Ordering.Infrastructure.Persistence;
using EmberDelivery.Ordering.Infrastructure.Persistence.Projections;
using EmberDelivery.Ordering.IntegrationTests.TestData;

using FluentAssertions;

using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;
using Xunit.Sdk;
using Xunit.v3;

using static EmberDelivery.Ordering.Api.Endpoints.Coupons.CreateCouponEndpoint;
using static EmberDelivery.Ordering.Api.Endpoints.Orders.CreateOrderEndpoint;

[assembly: Parallelization(Mode = ParallelMode.None)]

namespace EmberDelivery.Ordering.IntegrationTests.Infrastructure;

[Collection("OrderingIntegration")]
public abstract class BaseIntegrationTest(OrderingAppFixture fixture) : IAsyncLifetime
{
    private static readonly TimeSpan EventualConsistencyTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(200);

    protected OrderingAppFixture Fixture { get; } = fixture;

    protected HttpClient HttpClient { get; } = fixture.CreateClient();

    protected Guid CustomerId { get; private set; }

    protected static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public virtual async ValueTask InitializeAsync()
    {
        await Fixture.ResetDatabaseAsync();
        Fixture.Services.GetRequiredService<ConcurrentWriteSimulator>().Disarm();

        CustomerId = Guid.NewGuid();
        AuthenticateAsCustomer(CustomerId);
    }

    public virtual ValueTask DisposeAsync()
    {
        HttpClient.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    protected void AuthenticateAsCustomer(Guid customerId)
    {
        Authenticate(HttpClient, customerId, TestRoles.Customer);
    }

    protected void ClearAuthentication()
    {
        HttpClient.DefaultRequestHeaders.Authorization = null;
        HttpClient.DefaultRequestHeaders.Remove(TestAuthHandler.RolesHeaderName);
    }

    protected HttpClient CreateClientFor(Guid userId, params string[] roles)
    {
        HttpClient client = Fixture.CreateClient();
        Authenticate(client, userId, roles);
        return client;
    }

    protected async Task ExecuteScopeAsync(Func<IServiceProvider, Task> action)
    {
        await using AsyncServiceScope scope = Fixture.Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    protected async Task<T> ExecuteScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using AsyncServiceScope scope = Fixture.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    protected Task<T> QueryDbAsync<T>(Func<OrderingDbContext, Task<T>> query) =>
        ExecuteScopeAsync(serviceProvider => query(serviceProvider.GetRequiredService<OrderingDbContext>()));

    protected async Task<Guid> SeedRestaurantMenuAsync(bool isAcceptingOrders = true, string currency = "UAH")
    {
        var restaurantId = Guid.NewGuid();

        await ExecuteScopeAsync(async serviceProvider =>
        {
            OrderingDbContext dbContext = serviceProvider.GetRequiredService<OrderingDbContext>();
            dbContext.RestaurantMenuSnapshots.Add(new RestaurantMenuSnapshotRecord
            {
                RestaurantId = restaurantId,
                PayloadJson = TestMenu.CreatePayloadJson(restaurantId, isAcceptingOrders, currency),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync(CancellationToken);
        });

        return restaurantId;
    }

    protected async Task<Guid> CreateOrderAsync(
        Guid restaurantId,
        string? couponCode = null,
        decimal expectedTotalAmount = TestMenu.StandardBasketTotal,
        string currency = "UAH")
    {
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "api/v1/orders",
            TestMenu.StandardOrderRequest(restaurantId, expectedTotalAmount, couponCode, currency),
            CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(CancellationToken));

        CreateOrderResponse? body = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(CancellationToken);
        return body!.Id;
    }

    // Upstream services (Billing, Kitchen, Dispatch) do not exist yet, so later lifecycle states are reached
    // by running the same domain transitions the saga consumers would trigger, in their own unit of work.
    protected Task ApplyToOrderAsync(Guid orderId, Func<Order, TimeProvider, Result> transition) =>
        ExecuteScopeAsync(async serviceProvider =>
        {
            IOrderRepository orderRepository = serviceProvider.GetRequiredService<IOrderRepository>();
            TimeProvider timeProvider = serviceProvider.GetRequiredService<TimeProvider>();

            Order? order = await orderRepository.GetByIdAsync(orderId, CancellationToken);
            order.Should().NotBeNull();

            Result result = transition(order, timeProvider);
            result.IsSuccess.Should().BeTrue(result.Error.Description);

            await serviceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken);
        });

    protected Task MoveOrderToCookingAsync(Guid orderId) =>
        ApplyToOrderAsync(orderId, (order, timeProvider) =>
        {
            Result paid = order.MarkAsPaid();
            return paid.IsFailure ? paid : order.MarkAsCooking(estimatedPrepMinutes: 20, timeProvider);
        });

    protected Task<Order> GetOrderFromDbAsync(Guid orderId) =>
        QueryDbAsync(db => db.Orders.AsNoTracking().SingleAsync(x => x.Id == orderId, CancellationToken));

    protected async Task<Guid> CreateCouponAsync(CreateCouponRequest request)
    {
        using HttpClient adminClient = CreateClientFor(Guid.NewGuid(), TestRoles.Admin);

        HttpResponseMessage response = await adminClient.PostAsJsonAsync("api/v1/coupons", request, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(CancellationToken));

        CreateCouponResponse? body = await response.Content.ReadFromJsonAsync<CreateCouponResponse>(CancellationToken);
        return body!.Id;
    }

    protected Task<Guid> CreatePercentageCouponAsync(string code, decimal percentage, int? maxTotalRedemptions = null) =>
        CreateCouponAsync(new CreateCouponRequest(
            code,
            DiscountType.Percentage,
            percentage,
            MinimumBasketSubtotal: 0m,
            MaxTotalRedemptions: maxTotalRedemptions));

    // Publishing goes through a real broker, so the test must wait until the consumer has committed its inbox record;
    // asserting on the database any earlier would race against message delivery.
    protected async Task PublishAndWaitForConsumptionAsync<T>(T message)
        where T : class
    {
        Guid messageId = NewId.NextGuid();

        IBus bus = Fixture.Services.GetRequiredService<IBus>();
        await bus.Publish(message, context => context.MessageId = messageId, CancellationToken);

        await WaitUntilAsync(
            dbContext => dbContext.Set<InboxState>().AnyAsync(
                inbox => inbox.MessageId == messageId && inbox.Delivered != null,
                CancellationToken),
            $"{typeof(T).Name} with message id {messageId} to be consumed");
    }

    protected async Task WaitUntilAsync(Func<OrderingDbContext, Task<bool>> condition, string awaitedOutcome)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        timeout.CancelAfter(EventualConsistencyTimeout);

        while (!timeout.IsCancellationRequested)
        {
            if (await QueryDbAsync(condition))
            {
                return;
            }

            await Task.Delay(PollingInterval, CancellationToken);
        }

        throw new TimeoutException($"Timed out after {EventualConsistencyTimeout.TotalSeconds}s waiting for {awaitedOutcome}.");
    }

    private static void Authenticate(HttpClient client, Guid userId, params string[] roles)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(TestAuthHandler.SchemeName, userId.ToString());

        client.DefaultRequestHeaders.Remove(TestAuthHandler.RolesHeaderName);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeaderName, string.Join(',', roles));
    }
}
