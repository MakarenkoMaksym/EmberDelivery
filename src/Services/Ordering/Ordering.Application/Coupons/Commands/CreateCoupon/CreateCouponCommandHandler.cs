using EmberDelivery.BuildingBlocks.Application.Abstractions;
using EmberDelivery.BuildingBlocks.Application.Messaging;
using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.Ordering.Domain.Aggregates.Coupons;
using EmberDelivery.Ordering.Domain.ValueObjects;

namespace EmberDelivery.Ordering.Application.Coupons.Commands.CreateCoupon;

public sealed record CreateCouponCommand(
    string Code,
    DiscountType Type,
    decimal Value,
    decimal MinimumBasketSubtotal,
    int MaxRedemptionsPerCustomer = 1,
    int? MaxTotalRedemptions = null,
    decimal? MaxDiscountCap = null,
    DateTimeOffset? ExpiresAtUtc = null,
    Guid? RestrictedToRestaurantId = null,
    string Currency = "UAH") : ICommand<Guid>;

internal sealed class CreateCouponCommandHandler(
    ICouponRepository couponRepository,
    IUnitOfWork unitOfWork) : ICommandHandler<CreateCouponCommand, Guid>
{
    public async Task<Result<Guid>> Handle(
        CreateCouponCommand request,
        CancellationToken cancellationToken)
    {
        string normalizedCode = request.Code.Trim().ToUpperInvariant();
        Coupon? existing = await couponRepository.GetActiveByCodeAsync(normalizedCode, cancellationToken);

        if (existing is not null)
        {
            return Result.Failure<Guid>(
                Error.Conflict("Coupon.CodeAlreadyExists", $"An active coupon with code '{normalizedCode}' already exists."));
        }

        Money minBasket = new(request.MinimumBasketSubtotal, request.Currency);
        Money? maxCap = request.MaxDiscountCap.HasValue
            ? new Money(request.MaxDiscountCap.Value, request.Currency)
            : null;

        Result<Coupon> couponResult = Coupon.Create(
            normalizedCode,
            request.Type,
            request.Value,
            minBasket,
            request.MaxRedemptionsPerCustomer,
            request.MaxTotalRedemptions,
            maxCap,
            request.ExpiresAtUtc,
            request.RestrictedToRestaurantId);

        if (couponResult.IsFailure)
        {
            return Result.Failure<Guid>(couponResult.Error);
        }

        couponRepository.Add(couponResult.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(couponResult.Value.Id);
    }
}
