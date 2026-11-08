using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.Ordering.Domain.Aggregates.Coupons;
using EmberDelivery.Ordering.Domain.ValueObjects;

using FluentAssertions;

using Microsoft.Extensions.Time.Testing;

namespace EmberDelivery.Ordering.UnitTests.Domain.Aggregates.Coupons;

public sealed class CouponTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static Coupon CreateCoupon(
        string code = "SAVE",
        DiscountType type = DiscountType.FixedAmount,
        decimal value = 50m,
        decimal minimumBasket = 100m,
        int maxRedemptionsPerCustomer = 1,
        int? maxTotalRedemptions = null,
        Money? maxDiscountCap = null,
        DateTimeOffset? expiresAtUtc = null,
        Guid? restrictedToRestaurantId = null) =>
        Coupon.Create(
            code,
            type,
            value,
            new Money(minimumBasket),
            maxRedemptionsPerCustomer,
            maxTotalRedemptions,
            maxDiscountCap,
            expiresAtUtc,
            restrictedToRestaurantId).Value;

    // Version bumps once per unit of work, so arranged mutations must be "committed" before the Act step is measured.
    private static void SimulateUnitOfWorkCommit(Coupon coupon) => coupon.ClearDomainEvents();

    [Fact]
    public void Create_WithValidInput_ShouldNormalizeCodeAndStartActiveWithoutRedemptions()
    {
        // Arrange
        var restaurantId = Guid.NewGuid();

        // Act
        Result<Coupon> result = Coupon.Create(
            "  save20 ",
            DiscountType.Percentage,
            20m,
            new Money(300m),
            maxRedemptionsPerCustomer: 2,
            maxTotalRedemptions: 100,
            maxDiscountCap: new Money(120m),
            expiresAtUtc: NowUtc.AddDays(1),
            restrictedToRestaurantId: restaurantId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        Coupon coupon = result.Value;
        coupon.Code.Should().Be("SAVE20");
        coupon.Type.Should().Be(DiscountType.Percentage);
        coupon.Value.Should().Be(20m);
        coupon.MinimumBasketSubtotal.Should().Be(new Money(300m));
        coupon.MaxDiscountCap.Should().Be(new Money(120m));
        coupon.ExpiresAtUtc.Should().Be(NowUtc.AddDays(1));
        coupon.RestrictedToRestaurantId.Should().Be(restaurantId);
        coupon.MaxTotalRedemptions.Should().Be(100);
        coupon.MaxRedemptionsPerCustomer.Should().Be(2);
        coupon.TotalRedemptionsCount.Should().Be(0);
        coupon.IsActive.Should().BeTrue();
        coupon.Id.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenCodeIsBlank_ShouldReturnInvalidCodeFailure(string code)
    {
        // Act
        Result<Coupon> result = Coupon.Create(code, DiscountType.FixedAmount, 50m, new Money(100m));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CouponErrors.InvalidCode);
    }

    [Theory]
    [InlineData(DiscountType.FixedAmount, 0)]
    [InlineData(DiscountType.FixedAmount, -10)]
    [InlineData(DiscountType.Percentage, 0)]
    [InlineData(DiscountType.Percentage, 100.01)]
    [InlineData(DiscountType.FixedAmount, 10.005)]
    [InlineData(DiscountType.Percentage, 12.345)]
    public void Create_WhenDiscountValueIsOutOfRange_ShouldReturnInvalidDiscountValueFailure(
        DiscountType type,
        double value)
    {
        // Act
        Result<Coupon> result = Coupon.Create("CODE", type, (decimal)value, new Money(100m));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CouponErrors.InvalidDiscountValue);
    }

    [Theory]
    [InlineData(DiscountType.Percentage, 100)]
    [InlineData(DiscountType.FixedAmount, 500)]
    public void Create_WhenDiscountValueIsAtAllowedUpperBound_ShouldSucceed(DiscountType type, double value)
    {
        // Act
        Result<Coupon> result = Coupon.Create("CODE", type, (decimal)value, new Money(100m));

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    [InlineData(1, 0)]
    [InlineData(1, -5)]
    public void Create_WhenUsageLimitsAreNotPositive_ShouldReturnInvalidUsageLimitsFailure(
        int maxPerCustomer,
        int? maxTotal)
    {
        // Act
        Result<Coupon> result = Coupon.Create(
            "CODE",
            DiscountType.FixedAmount,
            50m,
            new Money(100m),
            maxPerCustomer,
            maxTotal);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CouponErrors.InvalidUsageLimits);
    }

    [Fact]
    public void EvaluateDiscount_WhenFixedAmount_ShouldReturnFixedValue()
    {
        // Arrange
        Coupon coupon = CreateCoupon(type: DiscountType.FixedAmount, value: 50m);

        // Act
        Result<Money> result = coupon.EvaluateDiscount(Guid.NewGuid(), new Money(400m), 0, NowUtc);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new Money(50m));
    }

    [Fact]
    public void EvaluateDiscount_WhenPercentage_ShouldReturnShareOfSubtotal()
    {
        // Arrange
        Coupon coupon = CreateCoupon(type: DiscountType.Percentage, value: 10m);

        // Act
        Result<Money> result = coupon.EvaluateDiscount(Guid.NewGuid(), new Money(400m), 0, NowUtc);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new Money(40m));
    }

    [Fact]
    public void EvaluateDiscount_WhenPercentageProducesFractionalCents_ShouldTruncateTowardZero()
    {
        // Arrange: 15% of 99.99 is 14.9985; commercial rounding would give 15.00, truncation keeps 14.99.
        Coupon coupon = CreateCoupon(type: DiscountType.Percentage, value: 15m, minimumBasket: 50m);

        // Act
        Result<Money> result = coupon.EvaluateDiscount(Guid.NewGuid(), new Money(99.99m), 0, NowUtc);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new Money(14.99m));
    }

    [Fact]
    public void EvaluateDiscount_WhenPercentageExceedsMaxCap_ShouldCapDiscountAtMaxLimit()
    {
        // Arrange
        Coupon coupon = CreateCoupon(
            type: DiscountType.Percentage,
            value: 20m,
            minimumBasket: 300m,
            maxDiscountCap: new Money(120m));

        // Act
        Result<Money> result = coupon.EvaluateDiscount(Guid.NewGuid(), new Money(1000m), 0, NowUtc);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new Money(120m));
    }

    [Fact]
    public void EvaluateDiscount_WhenFixedAmountExceedsSubtotal_ShouldNotDiscountMoreThanSubtotal()
    {
        // Arrange
        Coupon coupon = CreateCoupon(type: DiscountType.FixedAmount, value: 100m, minimumBasket: 50m);

        // Act
        Result<Money> result = coupon.EvaluateDiscount(Guid.NewGuid(), new Money(80m), 0, NowUtc);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new Money(80m));
    }

    [Fact]
    public void EvaluateDiscount_WhenCouponIsDeactivated_ShouldReturnInactiveFailure()
    {
        // Arrange
        Coupon coupon = CreateCoupon();
        coupon.Deactivate();

        // Act
        Result<Money> result = coupon.EvaluateDiscount(Guid.NewGuid(), new Money(400m), 0, NowUtc);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CouponErrors.Inactive(coupon.Code));
    }

    [Fact]
    public void EvaluateDiscount_WhenCurrentTimeIsAfterExpiration_ShouldReturnExpiredFailure()
    {
        // Arrange
        Coupon coupon = CreateCoupon(expiresAtUtc: NowUtc.AddTicks(-1));

        // Act
        Result<Money> result = coupon.EvaluateDiscount(Guid.NewGuid(), new Money(400m), 0, NowUtc);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CouponErrors.Expired(coupon.Code));
    }

    [Fact]
    public void EvaluateDiscount_WhenCurrentTimeEqualsExpiration_ShouldStillBeValid()
    {
        // Arrange
        Coupon coupon = CreateCoupon(expiresAtUtc: NowUtc);

        // Act
        Result<Money> result = coupon.EvaluateDiscount(Guid.NewGuid(), new Money(400m), 0, NowUtc);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void EvaluateDiscount_WhenTotalRedemptionsReachedLimit_ShouldReturnTotalUsageLimitReachedFailure()
    {
        // Arrange
        Coupon coupon = CreateCoupon(maxTotalRedemptions: 1);
        FakeTimeProvider timeProvider = new(NowUtc);
        coupon.Redeem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new Money(400m), 0, timeProvider);

        // Act
        Result<Money> result = coupon.EvaluateDiscount(Guid.NewGuid(), new Money(400m), 0, NowUtc);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CouponErrors.TotalUsageLimitReached(coupon.Code));
    }

    [Fact]
    public void EvaluateDiscount_WhenCustomerReachedPersonalLimit_ShouldReturnCustomerUsageLimitReachedFailure()
    {
        // Arrange
        Coupon coupon = CreateCoupon(maxRedemptionsPerCustomer: 2);

        // Act
        Result<Money> result = coupon.EvaluateDiscount(Guid.NewGuid(), new Money(400m), 2, NowUtc);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CouponErrors.CustomerUsageLimitReached(coupon.Code));
    }

    [Fact]
    public void EvaluateDiscount_WhenCouponIsRestrictedToAnotherRestaurant_ShouldReturnNotValidForRestaurantFailure()
    {
        // Arrange
        Coupon coupon = CreateCoupon(restrictedToRestaurantId: Guid.NewGuid());

        // Act
        Result<Money> result = coupon.EvaluateDiscount(Guid.NewGuid(), new Money(400m), 0, NowUtc);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CouponErrors.NotValidForRestaurant(coupon.Code));
    }

    [Fact]
    public void EvaluateDiscount_WhenCouponIsRestrictedToSameRestaurant_ShouldSucceed()
    {
        // Arrange
        var restaurantId = Guid.NewGuid();
        Coupon coupon = CreateCoupon(restrictedToRestaurantId: restaurantId);

        // Act
        Result<Money> result = coupon.EvaluateDiscount(restaurantId, new Money(400m), 0, NowUtc);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void EvaluateDiscount_WhenSubtotalIsBelowMinimumBasket_ShouldReturnMinimumBasketNotMetFailure()
    {
        // Arrange
        Coupon coupon = CreateCoupon(minimumBasket: 300m);

        // Act
        Result<Money> result = coupon.EvaluateDiscount(Guid.NewGuid(), new Money(299.99m), 0, NowUtc);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CouponErrors.MinimumBasketNotMet(coupon.Code, 300m, 299.99m));
    }

    [Fact]
    public void EvaluateDiscount_WhenCalled_ShouldNotConsumeRedemption()
    {
        // Arrange
        Coupon coupon = CreateCoupon();
        uint initialVersion = coupon.Version;

        // Act
        coupon.EvaluateDiscount(Guid.NewGuid(), new Money(400m), 0, NowUtc);

        // Assert
        coupon.TotalRedemptionsCount.Should().Be(0);
        coupon.Version.Should().Be(initialVersion);
    }

    [Fact]
    public void Redeem_WhenDiscountIsApplicable_ShouldReturnRedemptionAndIncrementUsageAndVersion()
    {
        // Arrange
        FakeTimeProvider timeProvider = new(NowUtc);
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        Coupon coupon = CreateCoupon(type: DiscountType.FixedAmount, value: 100m, minimumBasket: 200m);
        uint initialVersion = coupon.Version;

        // Act
        Result<CouponRedemption> result = coupon.Redeem(
            customerId,
            orderId,
            restaurantId,
            new Money(350m),
            customerPreviousRedemptions: 0,
            timeProvider);

        // Assert
        result.IsSuccess.Should().BeTrue();
        CouponRedemption redemption = result.Value;
        redemption.CouponId.Should().Be(coupon.Id);
        redemption.CustomerId.Should().Be(customerId);
        redemption.OrderId.Should().Be(orderId);
        redemption.DiscountApplied.Should().Be(new Money(100m));
        redemption.RedeemedAtUtc.Should().Be(NowUtc);
        coupon.TotalRedemptionsCount.Should().Be(1);
        coupon.Version.Should().Be(initialVersion + 1);
    }

    [Fact]
    public void Redeem_WhenCouponIsExhausted_ShouldReturnFailureAndKeepUsageCount()
    {
        // Arrange
        FakeTimeProvider timeProvider = new(NowUtc);
        Coupon coupon = CreateCoupon(maxTotalRedemptions: 1);
        coupon.Redeem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new Money(400m), 0, timeProvider);
        SimulateUnitOfWorkCommit(coupon);
        uint initialVersion = coupon.Version;

        // Act
        Result<CouponRedemption> result = coupon.Redeem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new Money(400m),
            0,
            timeProvider);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CouponErrors.TotalUsageLimitReached(coupon.Code));
        coupon.TotalRedemptionsCount.Should().Be(1);
        coupon.Version.Should().Be(initialVersion);
    }

    [Fact]
    public void Redeem_WhenCouponHasExpired_ShouldReturnExpiredFailureAndKeepVersion()
    {
        // Arrange
        FakeTimeProvider timeProvider = new(NowUtc);
        Coupon coupon = CreateCoupon(expiresAtUtc: NowUtc.AddMinutes(-1));
        uint initialVersion = coupon.Version;

        // Act
        Result<CouponRedemption> result = coupon.Redeem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new Money(400m),
            0,
            timeProvider);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CouponErrors.Expired(coupon.Code));
        coupon.TotalRedemptionsCount.Should().Be(0);
        coupon.Version.Should().Be(initialVersion);
    }

    [Fact]
    public void ReleaseRedemption_WhenRedemptionBelongsToCoupon_ShouldRestoreAvailableUsage()
    {
        // Arrange
        FakeTimeProvider timeProvider = new(NowUtc);
        var restaurantId = Guid.NewGuid();
        Coupon coupon = CreateCoupon(maxTotalRedemptions: 1);
        CouponRedemption redemption = coupon.Redeem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            restaurantId,
            new Money(350m),
            0,
            timeProvider).Value;
        SimulateUnitOfWorkCommit(coupon);
        uint initialVersion = coupon.Version;

        // Act
        coupon.ReleaseRedemption(redemption);

        // Assert
        coupon.TotalRedemptionsCount.Should().Be(0);
        coupon.Version.Should().Be(initialVersion + 1);
        coupon.EvaluateDiscount(restaurantId, new Money(350m), 0, NowUtc).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ReleaseRedemption_WhenNothingIsRedeemed_ShouldKeepCountAtZero()
    {
        // Arrange
        FakeTimeProvider timeProvider = new(NowUtc);
        Coupon coupon = CreateCoupon();
        CouponRedemption redemption = coupon.Redeem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new Money(350m),
            0,
            timeProvider).Value;
        coupon.ReleaseRedemption(redemption);
        SimulateUnitOfWorkCommit(coupon);
        uint initialVersion = coupon.Version;

        // Act
        coupon.ReleaseRedemption(redemption);

        // Assert
        coupon.TotalRedemptionsCount.Should().Be(0);
        coupon.Version.Should().Be(initialVersion);
    }

    [Fact]
    public void ReleaseRedemption_WhenRedemptionBelongsToAnotherCoupon_ShouldThrowInvalidOperationException()
    {
        // Arrange
        FakeTimeProvider timeProvider = new(NowUtc);
        Coupon coupon = CreateCoupon(code: "ONE");
        Coupon anotherCoupon = CreateCoupon(code: "TWO");
        CouponRedemption foreignRedemption = anotherCoupon.Redeem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new Money(350m),
            0,
            timeProvider).Value;

        // Act
        Action act = () => coupon.ReleaseRedemption(foreignRedemption);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Deactivate_WhenCouponIsActive_ShouldDeactivateAndIncrementVersion()
    {
        // Arrange
        Coupon coupon = CreateCoupon();
        uint initialVersion = coupon.Version;

        // Act
        coupon.Deactivate();

        // Assert
        coupon.IsActive.Should().BeFalse();
        coupon.Version.Should().Be(initialVersion + 1);
    }

    [Fact]
    public void Deactivate_WhenAlreadyDeactivated_ShouldNotChangeVersion()
    {
        // Arrange
        Coupon coupon = CreateCoupon();
        coupon.Deactivate();
        SimulateUnitOfWorkCommit(coupon);
        uint initialVersion = coupon.Version;

        // Act
        coupon.Deactivate();

        // Assert
        coupon.IsActive.Should().BeFalse();
        coupon.Version.Should().Be(initialVersion);
    }
}
