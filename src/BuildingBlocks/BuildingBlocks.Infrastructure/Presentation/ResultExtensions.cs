using EmberDelivery.BuildingBlocks.Domain.Results;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EmberDelivery.BuildingBlocks.Infrastructure.Presentation;

public static class ResultExtensions
{
    public static IResult Match<T>(this Result<T> result, Func<T, IResult> onSuccess)
    {
        return result.IsSuccess ? onSuccess(result.Value) : result.ToProblemDetails();
    }

    public static IResult Match(this Result result, Func<IResult> onSuccess)
    {
        return result.IsSuccess ? onSuccess() : result.ToProblemDetails();
    }

    private static IResult ToProblemDetails(this Result result)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException("Cannot convert a successful result to ProblemDetails.");
        }

        ProblemDetails problemDetails = new()
        {
            Status = GetStatusCode(result.Error.Type),
            Title = GetTitle(result.Error.Type),
            Type = GetType(result.Error.Type),
            Detail = result.Error.Description
        };

        if (result.Error is ValidationError validationError)
        {
            problemDetails.Extensions.Add("errors", validationError.Errors);
        }
        else
        {
            problemDetails.Extensions.Add("errors", new[] { result.Error });
        }

        return Results.Problem(problemDetails);
    }

    private static int GetStatusCode(ErrorType errorType) => errorType switch
    {
        ErrorType.Validation or ErrorType.Problem => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError
    };

    private static string GetTitle(ErrorType errorType) => errorType switch
    {
        ErrorType.Validation => "Bad Request",
        ErrorType.Problem => "Bad Request",
        ErrorType.Unauthorized => "Unauthorized",
        ErrorType.Forbidden => "Forbidden",
        ErrorType.NotFound => "Not Found",
        ErrorType.Conflict => "Conflict",
        _ => "Server Error"
    };

    private static string GetType(ErrorType errorType) => errorType switch
    {
        ErrorType.Validation or ErrorType.Problem => "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.1",
        ErrorType.Unauthorized => "https://datatracker.ietf.org/doc/html/rfc7235#section-3.1",
        ErrorType.Forbidden => "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.3",
        ErrorType.NotFound => "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.4",
        ErrorType.Conflict => "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.8",
        _ => "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1"
    };
}
