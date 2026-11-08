using FluentValidation;

namespace EmberDelivery.Ordering.Application.Coupons.Commands.CreateCoupon;

internal sealed class CreateCouponCommandValidator : AbstractValidator<CreateCouponCommand>
{
    public CreateCouponCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Currency).NotEmpty().Length(3);

        // Money rejects sub-cent amounts with an exception, so malformed input must be stopped here as a 400.
        RuleFor(x => x.MinimumBasketSubtotal)
            .GreaterThanOrEqualTo(0m)
            .PrecisionScale(18, 2, ignoreTrailingZeros: true);

        RuleFor(x => x.MaxDiscountCap!.Value)
            .GreaterThanOrEqualTo(0m)
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .OverridePropertyName(nameof(CreateCouponCommand.MaxDiscountCap))
            .When(x => x.MaxDiscountCap.HasValue);
    }
}
