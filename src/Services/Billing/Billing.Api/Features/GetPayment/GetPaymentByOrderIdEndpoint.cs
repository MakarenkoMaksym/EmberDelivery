using EmberDelivery.Billing.Api.Domain.PaymentHolds;
using EmberDelivery.Billing.Api.Extensions;
using EmberDelivery.Billing.Api.Infrastructure.Persistence;
using EmberDelivery.BuildingBlocks.Application.Abstractions.Identity;
using EmberDelivery.BuildingBlocks.Domain.Results;
using EmberDelivery.BuildingBlocks.Infrastructure.Presentation;

namespace EmberDelivery.Billing.Api.Features.GetPayment;

public sealed class GetPaymentByOrderIdEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("payments/by-order/{orderId:guid}", async (
            Guid orderId,
            BillingDbContext dbContext,
            ICurrentUserService currentUser,
            CancellationToken cancellationToken) =>
        {
            Result<PaymentResponse> result = await PaymentQuery.FindAsync(
                dbContext,
                hold => hold.OrderId == orderId,
                PaymentHoldErrors.NotFoundForOrder(orderId),
                currentUser,
                cancellationToken);

            return result.Match(Results.Ok);
        })
        .WithName("GetPaymentByOrderId")
        .WithTags(PaymentQuery.EndpointTag)
        .RequireAuthorization(BillingPolicies.CustomerOrAdmin)
        .Produces<PaymentResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
