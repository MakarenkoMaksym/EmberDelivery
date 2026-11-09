using System.Net;
using System.Net.Http.Json;

using FluentAssertions;

using Xunit;

namespace EmberDelivery.Ordering.IntegrationTests.Infrastructure;

public static class ProblemResponseAssertions
{
    public static async Task<ProblemResponse> ShouldBeProblemAsync(
        this HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string? expectedErrorCode = null)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        // The raw body is attached as the failure reason so an unexpected 500 shows its stack trace in the test output.
        response.StatusCode.Should().Be(expectedStatus, await response.Content.ReadAsStringAsync(cancellationToken));

        ProblemResponse? problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(cancellationToken);
        problem.Should().NotBeNull();
        problem.Status.Should().Be((int)expectedStatus);

        if (expectedErrorCode is not null)
        {
            problem.Errors.Should().ContainSingle(e => e.Code == expectedErrorCode);
        }

        return problem;
    }
}
