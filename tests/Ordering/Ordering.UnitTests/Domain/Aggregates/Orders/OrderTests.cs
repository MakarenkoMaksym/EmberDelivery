using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.Ordering.Domain.Aggregates.Coupons;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;
using EmberDelivery.Ordering.Domain.Aggregates.Orders.Events;
using EmberDelivery.Ordering.Domain.Pricing;
using EmberDelivery.Ordering.Domain.ValueObjects;
using EmberDelivery.Ordering.UnitTests.TestUtils;

using FluentAssertions;

namespace EmberDelivery.Ordering.UnitTests.Domain.Aggregates.Orders;

public sealed class OrderTests
{
    [Fact]
    public void Create_WithValidInputAndNoCoupon_ShouldCalculateTotalsAndRaiseOrderCreatedEvent()
    {
        // Arrange
        OrderTestBuilder builder = new();

        // Act
        Result<Order> result = builder.CreateOrder();

        // Assert
        result.IsSuccess.Should().BeTrue();
        Order order = result.Value;

        order.Id.Should().NotBeEmpty();
        order.CustomerId.Should().Be(builder.CustomerId);
        order.RestaurantId.Should().Be(builder.RestaurantId);
        order.Status.Should().Be(OrderStatus.Submitted);
        order.CreatedAtUtc.Should().Be(OrderTestBuilder.DefaultNowUtc);
        order.Lines.Should().ContainSingle();
        order.ItemsSubtotal.Should().Be(new Money(400m));
        order.DiscountAmount.Should().Be(Money.Zero());
        order.DeliveryFee.Should().Be(new Money(50m));
        order.TotalPrice.Should().Be(OrderTestBuilder.DefaultExpectedTotalPrice);
        order.AppliedCouponId.Should().BeNull();
        order.AppliedCouponCode.Should().BeNull();
        order.AssignedCourierId.Should().BeNull();
        order.CompletedAtUtc.Should().BeNull();
        order.EstimatedDeliveryAtUtc.Should().BeNull();
        order.Version.Should().Be(2);

        OrderCreatedDomainEvent createdEvent = order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderCreatedDomainEvent>().Subject;
        createdEvent.OrderId.Should().Be(order.Id);
        createdEvent.CustomerId.Should().Be(builder.CustomerId);
        createdEvent.RestaurantId.Should().Be(builder.RestaurantId);
        createdEvent.TotalPrice.Should().Be(OrderTestBuilder.DefaultExpectedTotalPrice);
        createdEvent.AppliedCouponId.Should().BeNull();
    }

    [Fact]
    public void Create_WhenBasketIsEmpty_ShouldReturnEmptyBasketFailure()
    {
        // Arrange
        OrderTestBuilder builder = new OrderTestBuilder()
            .WithLines();

        // Act
        Result<Order> result = builder.CreateOrder();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.EmptyBasket);
    }

    [Fact]
    public void Create_WhenSubtotalIsBelowMinimumBasketRequirement_ShouldReturnBelowMinimumBasketFailure()
    {
        // Arrange
        OrderLine singleItem = OrderTestBuilder.CreateDefaultOrderLine(new Money(250m), quantity: 1);
        OrderTestBuilder builder = new OrderTestBuilder()
            .WithMinimumBasketRequirement(new Money(300m))
            .WithLines(singleItem);

        // Act
        Result<Order> result = builder.CreateOrder();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.BelowMinimumBasket(300m, 250m));
    }

    [Fact]
    public void Create_WhenSubtotalEqualsMinimumBasketRequirement_ShouldSucceed()
    {
        // Arrange
        OrderLine singleItem = OrderTestBuilder.CreateDefaultOrderLine(new Money(300m), quantity: 1);
        OrderTestBuilder builder = new OrderTestBuilder()
            .WithMinimumBasketRequirement(new Money(300m))
            .WithLines(singleItem)
            .WithExpectedTotalPrice(new Money(350m));

        // Act
        Result<Order> result = builder.CreateOrder();

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_WhenSubtotalEqualsFreeDeliveryThreshold_ShouldChargeNoDeliveryFee()
    {
        // Arrange
        OrderLine item = OrderTestBuilder.CreateDefaultOrderLine(new Money(250m), quantity: 2);
        OrderTestBuilder builder = new OrderTestBuilder()
            .WithLines(item)
            .WithExpectedTotalPrice(new Money(500m));

        // Act
        Result<Order> result = builder.CreateOrder();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.DeliveryFee.Should().Be(Money.Zero());
        result.Value.TotalPrice.Should().Be(new Money(500m));
    }

    [Fact]
    public void Create_WithApplicableCoupon_ShouldApplyDiscountAndRecordCouponInOrderAndEvent()
    {
        // Arrange
        Coupon coupon = Coupon.Create(
            code: "MINUS50",
            type: DiscountType.FixedAmount,
            value: 50m,
            minimumBasketSubtotal: new Money(300m)).Value;

        OrderTestBuilder builder = new OrderTestBuilder()
            .WithCoupon(coupon)
            .WithExpectedTotalPrice(new Money(400m));

        // Act
        Result<Order> result = builder.CreateOrder();

        // Assert
        result.IsSuccess.Should().BeTrue();
        Order order = result.Value;
        order.ItemsSubtotal.Should().Be(new Money(400m));
        order.DiscountAmount.Should().Be(new Money(50m));
        order.DeliveryFee.Should().Be(new Money(50m));
        order.TotalPrice.Should().Be(new Money(400m));
        order.AppliedCouponId.Should().Be(coupon.Id);
        order.AppliedCouponCode.Should().Be("MINUS50");
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderCreatedDomainEvent>()
            .Which.AppliedCouponId.Should().Be(coupon.Id);
    }

    [Fact]
    public void Create_WithPercentageCouponProducingFractionalCents_ShouldUseTruncatedDiscountInTotal()
    {
        // Arrange: 10% of 333.33 is 33.333, truncated to 33.33, so the net is exactly 300.00 + 50.00 delivery.
        OrderLine item = OrderTestBuilder.CreateDefaultOrderLine(new Money(333.33m), quantity: 1);
        Coupon coupon = Coupon.Create(
            code: "TEN",
            type: DiscountType.Percentage,
            value: 10m,
            minimumBasketSubtotal: new Money(300m)).Value;

        OrderTestBuilder builder = new OrderTestBuilder()
            .WithLines(item)
            .WithCoupon(coupon)
            .WithExpectedTotalPrice(new Money(350m));

        // Act
        Result<Order> result = builder.CreateOrder();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.DiscountAmount.Should().Be(new Money(33.33m));
        result.Value.TotalPrice.Should().Be(new Money(350m));
    }

    [Fact]
    public void Create_WhenCouponReducesNetAmountBelowFreeDeliveryThreshold_ShouldStillKeepFreeDelivery()
    {
        // Arrange
        OrderLine item = OrderTestBuilder.CreateDefaultOrderLine(new Money(420m), quantity: 1);
        DeliveryPricingPolicy policy = new(
            baseFee: new Money(60m),
            surgeMultiplier: 1.0m,
            freeDeliveryThreshold: new Money(400m));

        Coupon coupon = Coupon.Create(
            code: "MINUS50",
            type: DiscountType.FixedAmount,
            value: 50m,
            minimumBasketSubtotal: new Money(300m)).Value;

        OrderTestBuilder builder = new OrderTestBuilder()
            .WithLines(item)
            .WithDeliveryPolicy(policy)
            .WithCoupon(coupon)
            .WithExpectedTotalPrice(new Money(370m));

        // Act
        Result<Order> result = builder.CreateOrder();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.DeliveryFee.Should().Be(Money.Zero());
        result.Value.DiscountAmount.Should().Be(new Money(50m));
        result.Value.TotalPrice.Should().Be(new Money(370m));
        result.Value.AppliedCouponId.Should().Be(coupon.Id);
    }

    [Fact]
    public void Create_WhenCouponEvaluationFails_ShouldPropagateCouponError()
    {
        // Arrange
        Coupon expiredCoupon = Coupon.Create(
            code: "OLD2025",
            type: DiscountType.FixedAmount,
            value: 50m,
            minimumBasketSubtotal: new Money(100m),
            expiresAtUtc: OrderTestBuilder.DefaultNowUtc.AddMinutes(-1)).Value;

        OrderTestBuilder builder = new OrderTestBuilder()
            .WithCoupon(expiredCoupon);

        // Act
        Result<Order> result = builder.CreateOrder();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CouponErrors.Expired(expiredCoupon.Code));
    }

    [Fact]
    public void Create_WhenCustomerExhaustedCouponLimit_ShouldPropagateCustomerUsageLimitFailure()
    {
        // Arrange
        Coupon coupon = Coupon.Create(
            code: "ONCE",
            type: DiscountType.FixedAmount,
            value: 50m,
            minimumBasketSubtotal: new Money(100m),
            maxRedemptionsPerCustomer: 1).Value;

        OrderTestBuilder builder = new OrderTestBuilder()
            .WithCoupon(coupon, previousCustomerUses: 1);

        // Act
        Result<Order> result = builder.CreateOrder();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CouponErrors.CustomerUsageLimitReached(coupon.Code));
    }

    [Fact]
    public void Create_WhenActualTotalDiffersFromExpectedTotalPrice_ShouldReturnPriceChangedFailure()
    {
        // Arrange
        OrderTestBuilder builder = new OrderTestBuilder()
            .WithExpectedTotalPrice(new Money(400m));

        // Act
        Result<Order> result = builder.CreateOrder();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.PriceChanged(400m, 450m));
    }

    [Fact]
    public void MarkAsPaid_WhenOrderIsSubmitted_ShouldTransitionToPaid()
    {
        // Arrange
        Order order = new OrderTestBuilder().BuildSubmitted();
        uint initialVersion = order.Version;

        // Act
        Result result = order.MarkAsPaid();

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Paid);
        order.Version.Should().Be(initialVersion + 1);
    }

    [Fact]
    public void MarkAsPaid_WhenOrderIsAlreadyPaid_ShouldSucceedWithoutChanges()
    {
        // Arrange
        Order order = new OrderTestBuilder().BuildPaid();
        uint initialVersion = order.Version;

        // Act
        Result result = order.MarkAsPaid();

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Paid);
        order.Version.Should().Be(initialVersion);
        order.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(OrderStatus.Cooking)]
    [InlineData(OrderStatus.Delivering)]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public void MarkAsPaid_WhenOrderIsPastPaymentStage_ShouldReturnInvalidStateTransitionFailure(OrderStatus status)
    {
        // Arrange
        Order order = new OrderTestBuilder().BuildInState(status);
        uint initialVersion = order.Version;

        // Act
        Result result = order.MarkAsPaid();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.InvalidStateTransition(status, OrderStatus.Paid));
        order.Status.Should().Be(status);
        order.Version.Should().Be(initialVersion);
    }

    [Fact]
    public void MarkAsCooking_WhenOrderIsPaid_ShouldTransitionToCookingAndEstimateDelivery()
    {
        // Arrange
        OrderTestBuilder builder = new();
        Order order = builder.BuildPaid();
        uint initialVersion = order.Version;

        // Act
        Result result = order.MarkAsCooking(estimatedPrepMinutes: 20, builder.TimeProvider);

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cooking);
        order.EstimatedDeliveryAtUtc.Should().Be(OrderTestBuilder.DefaultNowUtc.AddMinutes(35));
        order.Version.Should().Be(initialVersion + 1);
    }

    [Fact]
    public void MarkAsCooking_WhenOrderIsAlreadyCooking_ShouldSucceedAndKeepOriginalEstimate()
    {
        // Arrange
        OrderTestBuilder builder = new();
        Order order = builder.BuildCooking();
        DateTimeOffset? originalEstimate = order.EstimatedDeliveryAtUtc;
        uint initialVersion = order.Version;
        builder.TimeProvider.Advance(TimeSpan.FromMinutes(10));

        // Act
        Result result = order.MarkAsCooking(estimatedPrepMinutes: 60, builder.TimeProvider);

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.EstimatedDeliveryAtUtc.Should().Be(originalEstimate);
        order.Version.Should().Be(initialVersion);
    }

    [Theory]
    [InlineData(OrderStatus.Submitted)]
    [InlineData(OrderStatus.Delivering)]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public void MarkAsCooking_WhenOrderIsNotPaid_ShouldReturnInvalidStateTransitionFailure(OrderStatus status)
    {
        // Arrange
        OrderTestBuilder builder = new();
        Order order = builder.BuildInState(status);
        uint initialVersion = order.Version;

        // Act
        Result result = order.MarkAsCooking(estimatedPrepMinutes: 20, builder.TimeProvider);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.InvalidStateTransition(status, OrderStatus.Cooking));
        order.Status.Should().Be(status);
        order.Version.Should().Be(initialVersion);
    }

    [Fact]
    public void MarkAsDelivering_WhenOrderIsCooking_ShouldTransitionToDeliveringAndAssignCourier()
    {
        // Arrange
        Order order = new OrderTestBuilder().BuildCooking();
        uint initialVersion = order.Version;
        var courierId = Guid.NewGuid();

        // Act
        Result result = order.MarkAsDelivering(courierId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Delivering);
        order.AssignedCourierId.Should().Be(courierId);
        order.Version.Should().Be(initialVersion + 1);
    }

    [Fact]
    public void MarkAsDelivering_WhenOrderIsAlreadyDelivering_ShouldSucceedAndKeepOriginalCourier()
    {
        // Arrange
        Order order = new OrderTestBuilder().BuildDelivering();
        uint initialVersion = order.Version;

        // Act
        Result result = order.MarkAsDelivering(Guid.NewGuid());

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.AssignedCourierId.Should().Be(OrderTestBuilder.DefaultCourierId);
        order.Version.Should().Be(initialVersion);
    }

    [Theory]
    [InlineData(OrderStatus.Submitted)]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public void MarkAsDelivering_WhenOrderIsNotCooking_ShouldReturnInvalidStateTransitionFailure(OrderStatus status)
    {
        // Arrange
        Order order = new OrderTestBuilder().BuildInState(status);
        uint initialVersion = order.Version;

        // Act
        Result result = order.MarkAsDelivering(Guid.NewGuid());

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.InvalidStateTransition(status, OrderStatus.Delivering));
        order.Status.Should().Be(status);
        order.Version.Should().Be(initialVersion);
    }

    [Fact]
    public void MarkAsCompleted_WhenOrderIsDelivering_ShouldCompleteAndRaiseOrderCompletedEvent()
    {
        // Arrange
        OrderTestBuilder builder = new();
        Order order = builder.BuildDelivering();
        uint initialVersion = order.Version;
        builder.TimeProvider.Advance(TimeSpan.FromMinutes(30));

        // Act
        Result result = order.MarkAsCompleted(builder.TimeProvider);

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Completed);
        order.CompletedAtUtc.Should().Be(OrderTestBuilder.DefaultNowUtc.AddMinutes(30));
        order.Version.Should().Be(initialVersion + 1);

        order.DomainEvents.Should().ContainSingle()
            .Which.Should().Be(new OrderCompletedDomainEvent(
                order.Id,
                builder.CustomerId,
                OrderTestBuilder.DefaultCourierId));
    }

    [Fact]
    public void MarkAsCompleted_WhenOrderIsAlreadyCompleted_ShouldSucceedWithoutRaisingEventAgain()
    {
        // Arrange
        OrderTestBuilder builder = new();
        Order order = builder.BuildCompleted();
        DateTimeOffset? originalCompletionTime = order.CompletedAtUtc;
        uint initialVersion = order.Version;
        builder.TimeProvider.Advance(TimeSpan.FromMinutes(30));

        // Act
        Result result = order.MarkAsCompleted(builder.TimeProvider);

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.CompletedAtUtc.Should().Be(originalCompletionTime);
        order.Version.Should().Be(initialVersion);
        order.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(OrderStatus.Submitted)]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Cooking)]
    [InlineData(OrderStatus.Cancelled)]
    public void MarkAsCompleted_WhenOrderIsNotDelivering_ShouldReturnInvalidStateTransitionFailure(OrderStatus status)
    {
        // Arrange
        OrderTestBuilder builder = new();
        Order order = builder.BuildInState(status);
        uint initialVersion = order.Version;

        // Act
        Result result = order.MarkAsCompleted(builder.TimeProvider);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.InvalidStateTransition(status, OrderStatus.Completed));
        order.Status.Should().Be(status);
        order.Version.Should().Be(initialVersion);
        order.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(OrderStatus.Submitted)]
    [InlineData(OrderStatus.Paid)]
    public void CancelByCustomer_WhenKitchenHasNotStarted_ShouldCancelAndRaiseCustomerInitiatedEvent(OrderStatus status)
    {
        // Arrange
        OrderTestBuilder builder = new();
        Order order = builder.BuildInState(status);
        uint initialVersion = order.Version;

        // Act
        Result result = order.CancelByCustomer(builder.CustomerId, "  Changed my mind  ");

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be("Changed my mind");
        order.Version.Should().Be(initialVersion + 1);
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().Be(new OrderCancelledDomainEvent(
                order.Id,
                builder.CustomerId,
                AppliedCouponId: null,
                Reason: "Changed my mind",
                IsCustomerInitiated: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CancelByCustomer_WhenReasonIsBlank_ShouldUseDefaultReason(string? reason)
    {
        // Arrange
        OrderTestBuilder builder = new();
        Order order = builder.BuildSubmitted();

        // Act
        Result result = order.CancelByCustomer(builder.CustomerId, reason);

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.CancellationReason.Should().Be("Cancelled by customer");
    }

    [Fact]
    public void CancelByCustomer_WhenOrderHasAppliedCoupon_ShouldCarryCouponIdInEvent()
    {
        // Arrange
        Coupon coupon = Coupon.Create("MINUS50", DiscountType.FixedAmount, 50m, new Money(300m)).Value;
        OrderTestBuilder builder = new OrderTestBuilder()
            .WithCoupon(coupon)
            .WithExpectedTotalPrice(new Money(400m));
        Order order = builder.BuildSubmitted();

        // Act
        Result result = order.CancelByCustomer(builder.CustomerId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderCancelledDomainEvent>()
            .Which.AppliedCouponId.Should().Be(coupon.Id);
    }

    [Fact]
    public void CancelByCustomer_WhenRequesterIsAnotherCustomer_ShouldReturnForbiddenCancellationFailure()
    {
        // Arrange
        Order order = new OrderTestBuilder().BuildSubmitted();
        uint initialVersion = order.Version;

        // Act
        Result result = order.CancelByCustomer(Guid.NewGuid());

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.ForbiddenCancellation);
        order.Status.Should().Be(OrderStatus.Submitted);
        order.Version.Should().Be(initialVersion);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void CancelByCustomer_WhenRequesterIsAnotherCustomerAndOrderIsCancelled_ShouldStillReturnForbiddenFailure()
    {
        // Arrange
        Order order = new OrderTestBuilder().BuildCancelled();

        // Act
        Result result = order.CancelByCustomer(Guid.NewGuid());

        // Assert
        result.Error.Should().Be(OrderErrors.ForbiddenCancellation);
    }

    [Fact]
    public void CancelByCustomer_WhenOrderIsAlreadyCancelled_ShouldSucceedWithoutRaisingEventAgain()
    {
        // Arrange
        OrderTestBuilder builder = new();
        Order order = builder.BuildCancelled();
        uint initialVersion = order.Version;

        // Act
        Result result = order.CancelByCustomer(builder.CustomerId, "Second attempt");

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.CancellationReason.Should().Be("Arranged cancellation");
        order.Version.Should().Be(initialVersion);
        order.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(OrderStatus.Cooking)]
    [InlineData(OrderStatus.Delivering)]
    [InlineData(OrderStatus.Completed)]
    public void CancelByCustomer_WhenKitchenHasStarted_ShouldReturnCannotCancelInProgressOrderFailure(OrderStatus status)
    {
        // Arrange
        OrderTestBuilder builder = new();
        Order order = builder.BuildInState(status);
        uint initialVersion = order.Version;

        // Act
        Result result = order.CancelByCustomer(builder.CustomerId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.CannotCancelInProgressOrder(status));
        order.Status.Should().Be(status);
        order.Version.Should().Be(initialVersion);
        order.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(OrderStatus.Submitted)]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Cooking)]
    [InlineData(OrderStatus.Delivering)]
    public void CancelBySystem_WhenOrderIsNotTerminal_ShouldCancelAndRaiseSystemInitiatedEvent(OrderStatus status)
    {
        // Arrange
        OrderTestBuilder builder = new();
        Order order = builder.BuildInState(status);
        uint initialVersion = order.Version;

        // Act
        Result result = order.CancelBySystem("  Kitchen out of salmon ");

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be("Kitchen out of salmon");
        order.Version.Should().Be(initialVersion + 1);
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().Be(new OrderCancelledDomainEvent(
                order.Id,
                builder.CustomerId,
                AppliedCouponId: null,
                Reason: "Kitchen out of salmon",
                IsCustomerInitiated: false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CancelBySystem_WhenReasonIsBlank_ShouldUseDefaultReason(string reason)
    {
        // Arrange
        Order order = new OrderTestBuilder().BuildSubmitted();

        // Act
        Result result = order.CancelBySystem(reason);

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.CancellationReason.Should().Be("Cancelled by system");
    }

    [Fact]
    public void CancelBySystem_WhenOrderIsAlreadyCancelled_ShouldSucceedWithoutRaisingEventAgain()
    {
        // Arrange
        Order order = new OrderTestBuilder().BuildCancelled();
        uint initialVersion = order.Version;

        // Act
        Result result = order.CancelBySystem("Duplicate retry signal");

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.CancellationReason.Should().Be("Arranged cancellation");
        order.Version.Should().Be(initialVersion);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void CancelBySystem_WhenOrderIsCompleted_ShouldReturnAlreadyTerminalStateFailure()
    {
        // Arrange
        Order order = new OrderTestBuilder().BuildCompleted();
        uint initialVersion = order.Version;

        // Act
        Result result = order.CancelBySystem("Too late");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.AlreadyTerminalState);
        order.Status.Should().Be(OrderStatus.Completed);
        order.Version.Should().Be(initialVersion);
        order.DomainEvents.Should().BeEmpty();
    }
}
