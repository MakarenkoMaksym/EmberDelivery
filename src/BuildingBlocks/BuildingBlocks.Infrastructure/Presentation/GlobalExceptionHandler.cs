using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

using Npgsql;

namespace EmberDelivery.BuildingBlocks.Infrastructure.Presentation;

public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IWebHostEnvironment env) : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ProblemDetails problemDetails = CreateProblemDetails(exception);

        httpContext.Response.StatusCode = problemDetails.Status!.Value;

        return problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception
        });
    }

    private ProblemDetails CreateProblemDetails(Exception exception)
    {
        switch (exception)
        {
            case DbUpdateConcurrencyException:
                return new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.8",
                    Title = "Concurrency Conflict",
                    Detail = "The resource was modified by another concurrent request. Please retry your operation."
                };

            // Check-then-insert races are settled by unique indexes; the constraint name and SQL stay server-side.
            case DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } }:
                return new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.8",
                    Title = "Conflict",
                    Detail = "The resource conflicts with an existing one. Refresh your data and retry."
                };

            default:
                bool includeDetails = env.IsDevelopment() || env.IsEnvironment("Testing");

                return new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1",
                    Title = "Internal Server Error",
                    Detail = includeDetails
                        ? exception.ToString()
                        : "An unexpected problem prevented us from fulfilling your request."
                };
        }
    }
}
