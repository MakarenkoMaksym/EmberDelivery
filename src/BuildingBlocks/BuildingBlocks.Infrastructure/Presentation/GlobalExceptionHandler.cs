using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace EmberDelivery.BuildingBlocks.Infrastructure.Presentation;

public sealed class GlobalExceptionHandler(IWebHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        bool includeDetails = env.IsDevelopment() || env.IsEnvironment("Testing");

        ProblemDetails problemDetails = new()
        {
            Status = StatusCodes.Status500InternalServerError,
            Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1",
            Title = "Internal Server Error",
            Detail = includeDetails
                ? exception.ToString()
                : "An unexpected problem prevented us from fulfilling your request."
        };

        if (exception is DbUpdateConcurrencyException)
        {
            problemDetails.Status = StatusCodes.Status409Conflict;
            problemDetails.Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.8";
            problemDetails.Title = "Concurrency Conflict";
            problemDetails.Detail = "The resource was modified by another concurrent request. Please retry your operation.";
        }

        httpContext.Response.StatusCode = problemDetails.Status.Value;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
