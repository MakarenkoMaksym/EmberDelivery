using System.Net;
using System.Net.Http.Json;

using EmberDelivery.Billing.Api.Domain;
using EmberDelivery.Billing.Api.Domain.PaymentHolds;
using EmberDelivery.Billing.Api.Features.GetPayment;
using EmberDelivery.Billing.Api.Infrastructure.Persistence;
using EmberDelivery.Billing.IntegrationTests.Infrastructure;
using EmberDelivery.BuildingBlocks.Infrastructure.Identity;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace EmberDelivery.Billing.IntegrationTests.Endpoints;

public sealed class GetPaymentEndpointsTests(BillingAppFixture fixture) : BaseIntegrationTest(fixture)
{
    private static readonly DateTimeOffset AuthorizedAtUtc = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetPaymentById_WhenRequestedByOwner_ShouldReturnPaymentWithLedger()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        PaymentHold hold = await SeedHeldPaymentAsync(customerId);
        using HttpClient client = CreateClientFor(customerId, Roles.Customer);

        // Act
        HttpResponseMessage response = await client.GetAsync($"api/v1/payments/{hold.Id}", CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));

        PaymentResponse? payment = await response.Content.ReadFromJsonAsync<PaymentResponse>(CancellationToken);
        payment.Should().NotBeNull();
        payment.PaymentHoldId.Should().Be(hold.Id);
        payment.OrderId.Should().Be(hold.OrderId);
        payment.CustomerId.Should().Be(customerId);
        payment.Status.Should().Be(nameof(PaymentHoldStatus.Held));
        payment.Amount.Should().Be(StandardAmount);
        payment.Currency.Should().Be(Currency);
        payment.LedgerEntries.Should().ContainSingle()
            .Which.Type.Should().Be(nameof(LedgerEntryType.Authorization));
    }

    [Fact]
    public async Task GetPaymentByOrderId_WhenRequestedByOwner_ShouldReturnPayment()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        PaymentHold hold = await SeedHeldPaymentAsync(customerId);
        using HttpClient client = CreateClientFor(customerId, Roles.Customer);

        // Act
        HttpResponseMessage response = await client.GetAsync($"api/v1/payments/by-order/{hold.OrderId}", CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<PaymentResponse>(CancellationToken))!.PaymentHoldId.Should().Be(hold.Id);
    }

    [Fact]
    public async Task GetPaymentById_WhenRequestedByAnotherCustomer_ShouldReturnForbidden()
    {
        // Arrange
        PaymentHold hold = await SeedHeldPaymentAsync(Guid.NewGuid());
        using HttpClient client = CreateClientFor(Guid.NewGuid(), Roles.Customer);

        // Act
        HttpResponseMessage response = await client.GetAsync($"api/v1/payments/{hold.Id}", CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetPaymentByOrderId_WhenRequestedByAdmin_ShouldReturnAnyCustomersPayment()
    {
        // Arrange
        PaymentHold hold = await SeedHeldPaymentAsync(Guid.NewGuid());
        using HttpClient client = CreateClientFor(Guid.NewGuid(), Roles.Admin);

        // Act
        HttpResponseMessage response = await client.GetAsync($"api/v1/payments/by-order/{hold.OrderId}", CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetPaymentById_WhenUserHasNoBillingRole_ShouldReturnForbidden()
    {
        // Arrange
        var courierId = Guid.NewGuid();
        PaymentHold hold = await SeedHeldPaymentAsync(courierId);
        using HttpClient client = CreateClientFor(courierId, Roles.Courier);

        // Act
        HttpResponseMessage response = await client.GetAsync($"api/v1/payments/{hold.Id}", CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("api/v1/payments/{0}")]
    [InlineData("api/v1/payments/by-order/{0}")]
    public async Task GetPayment_WhenPaymentDoesNotExist_ShouldReturnNotFound(string routeTemplate)
    {
        // Arrange
        using HttpClient client = CreateClientFor(Guid.NewGuid(), Roles.Customer);

        // Act
        HttpResponseMessage response = await client.GetAsync(
            string.Format(System.Globalization.CultureInfo.InvariantCulture, routeTemplate, Guid.NewGuid()),
            CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPaymentById_WhenUnauthenticated_ShouldReturnUnauthorized()
    {
        // Arrange
        PaymentHold hold = await SeedHeldPaymentAsync(Guid.NewGuid());
        using HttpClient client = Fixture.CreateClient();

        // Act
        HttpResponseMessage response = await client.GetAsync($"api/v1/payments/{hold.Id}", CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private Task<PaymentHold> SeedHeldPaymentAsync(Guid customerId) =>
        ExecuteScopeAsync(async serviceProvider =>
        {
            Money amount = Money.Create(StandardAmount, Currency).Value;
            PaymentHold hold = PaymentHold.Place(Guid.NewGuid(), customerId, amount, "auth_seeded", AuthorizedAtUtc).Value;

            BillingDbContext dbContext = serviceProvider.GetRequiredService<BillingDbContext>();
            dbContext.PaymentHolds.Add(hold);
            await dbContext.SaveChangesAsync(CancellationToken);

            return hold;
        });
}
