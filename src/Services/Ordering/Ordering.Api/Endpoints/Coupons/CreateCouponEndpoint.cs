using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.BuildingBlocks.Infrastructure.Identity;
using EmberDelivery.BuildingBlocks.Infrastructure.Presentation;
using EmberDelivery.Ordering.Application.Coupons.Commands.CreateCoupon;
using EmberDelivery.Ordering.Domain.Aggregates.Coupons;

using MediatR;

namespace EmberDelivery.Ordering.Api.Endpoints.Coupons;

public sealed class CreateCouponEndpoint : IEndpoint
{
    public sealed record CreateCouponRequest(
        string Code,
        DiscountType Type,
        decimal Value,
        decimal MinimumBasketSubtotal,
        int MaxRedemptionsPerCustomer = 1,
        int? MaxTotalRedemptions = null,
        decimal? MaxDiscountCap = null,
        DateTimeOffset? ExpiresAtUtc = null,
        Guid? RestrictedToRestaurantId = null,
        string Currency = "UAH");

    public sealed record CreateCouponResponse(Guid Id);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("coupons", async (
            CreateCouponRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            CreateCouponCommand command = new(
                request.Code,
                request.Type,
                request.Value,
                request.MinimumBasketSubtotal,
                request.MaxRedemptionsPerCustomer,
                request.MaxTotalRedemptions,
                request.MaxDiscountCap,
                request.ExpiresAtUtc,
                request.RestrictedToRestaurantId,
                request.Currency);

            Result<Guid> result = await sender.Send(command, cancellationToken);

            return result.Match(couponId =>
                Results.Created($"/api/v1/coupons/{couponId}", new CreateCouponResponse(couponId)));
        })
        .WithName("CreateCoupon")
        .WithTags(EndpointTags.Coupons)
        .RequireAuthorization(AuthorizationPolicies.RequireAdmin)
        .Produces<CreateCouponResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
