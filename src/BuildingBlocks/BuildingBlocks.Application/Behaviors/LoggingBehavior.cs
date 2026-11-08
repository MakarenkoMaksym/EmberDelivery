using EmberDelivery.BuildingBlocks.Domain.Results;

using MediatR;

using Microsoft.Extensions.Logging;

namespace EmberDelivery.BuildingBlocks.Application.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        string requestName = typeof(TRequest).Name;

        logger.LogInformation("Processing request {RequestName}", requestName);

        TResponse result = await next(cancellationToken);

        if (result.IsSuccess)
        {
            logger.LogInformation("Completed request {RequestName} successfully", requestName);
        }
        else
        {
            logger.LogWarning(
                "Request {RequestName} failed with error {ErrorCode}: {ErrorDescription}",
                requestName,
                result.Error.Code,
                result.Error.Description);
        }

        return result;
    }
}
