using System.Net;
using System.Net.Http.Json;

using EmberDelivery.Ordering.Domain.Aggregates.Coupons;
using EmberDelivery.Ordering.Domain.ValueObjects;
using EmberDelivery.Ordering.IntegrationTests.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

using static EmberDelivery.Ordering.Api.Endpoints.Coupons.CreateCouponEndpoint;

namespace EmberDelivery.Ordering.IntegrationTests.Endpoints.Coupons;

public sealed class CreateCouponEndpointTests(OrderingAppFixture fixture) : BaseIntegrationTest(fixture)
{
    private const string CouponsUrl = "api/v1/coupons";

    [Fact]
    public async Task CreateCoupon_AsAdmin_ShouldReturnCreatedAndPersistNormalizedActiveCoupon()
    {
        // Arrange
        using HttpClient adminClient = CreateClientFor(Guid.NewGuid(), TestRoles.Admin);
        CreateCouponRequest request = new(
            Code: "  summer25 ",
            Type: DiscountType.FixedAmount,
            Value: 25m,
            MinimumBasketSubtotal: 300m,
            MaxRedemptionsPerCustomer: 2,
            MaxTotalRedemptions: 100);

        // Act
        HttpResponseMessage response = await adminClient.PostAsJsonAsync(CouponsUrl, request, CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        CreateCouponResponse? body = await response.Content.ReadFromJsonAsync<CreateCouponResponse>(CancellationToken);
        response.Headers.Location!.OriginalString.Should().Be($"/api/v1/coupons/{body!.Id}");

        Coupon coupon = await QueryDbAsync(db => db.Coupons.SingleAsync(x => x.Id == body.Id, CancellationToken));
        coupon.Code.Should().Be("SUMMER25");
        coupon.Type.Should().Be(DiscountType.FixedAmount);
        coupon.Value.Should().Be(25m);
        coupon.MinimumBasketSubtotal.Amount.Should().Be(300m);
        coupon.MaxRedemptionsPerCustomer.Should().Be(2);
        coupon.MaxTotalRedemptions.Should().Be(100);
        coupon.TotalRedemptionsCount.Should().Be(0);
        coupon.IsActive.Should().BeTrue();
        coupon.Version.Should().Be(1);
    }

    [Fact]
    public async Task CreateCoupon_InEuroWithMaxDiscountCap_ShouldRoundTripCapInEuroThroughDatabase()
    {
        // Arrange
        using HttpClient adminClient = CreateClientFor(Guid.NewGuid(), TestRoles.Admin);
        CreateCouponRequest request = new(
            Code: "EURO15",
            Type: DiscountType.Percentage,
            Value: 15m,
            MinimumBasketSubtotal: 40m,
            MaxDiscountCap: 12.5m,
            Currency: "EUR");

        // Act
        HttpResponseMessage response = await adminClient.PostAsJsonAsync(CouponsUrl, request, CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        CreateCouponResponse? body = await response.Content.ReadFromJsonAsync<CreateCouponResponse>(CancellationToken);

        // A fresh DbContext guarantees the values are materialized from PostgreSQL, not served from the change tracker.
        Coupon coupon = await QueryDbAsync(db => db.Coupons.AsNoTracking().SingleAsync(x => x.Id == body!.Id, CancellationToken));
        coupon.MinimumBasketSubtotal.Should().Be(new Money(40m, "EUR"));
        coupon.MaxDiscountCap.Should().Be(new Money(12.5m, "EUR"));
    }

    [Fact]
    public async Task CreateCoupon_WhenActiveCouponWithSameCodeExists_ShouldReturnConflict()
    {
        // Arrange
        await CreatePercentageCouponAsync("DUPLICATE", percentage: 5m);
        using HttpClient adminClient = CreateClientFor(Guid.NewGuid(), TestRoles.Admin);

        // Act
        HttpResponseMessage response = await adminClient.PostAsJsonAsync(
            CouponsUrl,
            new CreateCouponRequest("duplicate", DiscountType.Percentage, 15m, MinimumBasketSubtotal: 0m),
            CancellationToken);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "Coupon.CodeAlreadyExists");
        (await QueryDbAsync(db => db.Coupons.CountAsync(CancellationToken))).Should().Be(1);
    }

    [Fact]
    public async Task CreateCoupon_WithMalformedAmounts_ShouldReturnBadRequestBeforeReachingDomain()
    {
        // Arrange
        // Sub-cent amounts would make the Money constructor throw, which must surface as 400, not 500.
        using HttpClient adminClient = CreateClientFor(Guid.NewGuid(), TestRoles.Admin);
        CreateCouponRequest request = new(
            Code: "",
            Type: DiscountType.FixedAmount,
            Value: 10m,
            MinimumBasketSubtotal: 100.005m,
            MaxDiscountCap: 20.001m);

        // Act
        HttpResponseMessage response = await adminClient.PostAsJsonAsync(CouponsUrl, request, CancellationToken);

        // Assert
        ProblemResponse problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        problem.Errors!.Select(e => e.Code).Should().BeEquivalentTo("Code", "MinimumBasketSubtotal", "MaxDiscountCap");
    }

    [Fact]
    public async Task CreateCoupon_WhenPercentageExceedsHundred_ShouldReturnBadRequestFromDomain()
    {
        // Arrange
        using HttpClient adminClient = CreateClientFor(Guid.NewGuid(), TestRoles.Admin);

        // Act
        HttpResponseMessage response = await adminClient.PostAsJsonAsync(
            CouponsUrl,
            new CreateCouponRequest("GREEDY", DiscountType.Percentage, 150m, MinimumBasketSubtotal: 0m),
            CancellationToken);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "Coupon.InvalidDiscountValue");
    }

    [Fact]
    public async Task CreateCoupon_AsCustomer_ShouldReturnForbidden()
    {
        // Act
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            CouponsUrl,
            new CreateCouponRequest("SNEAKY", DiscountType.Percentage, 50m, MinimumBasketSubtotal: 0m),
            CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await QueryDbAsync(db => db.Coupons.CountAsync(CancellationToken))).Should().Be(0);
    }
}
