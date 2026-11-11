using System.Reflection;

using EmberDelivery.ArchitectureTests.Infrastructure;
using EmberDelivery.Billing.Api.Infrastructure.Persistence;
using EmberDelivery.Ordering.Api.Endpoints;

using FluentAssertions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace EmberDelivery.ArchitectureTests;

// Reads metadata from the real EndpointDataSource of a booted host, so group-level conventions are taken into account
// exactly as the routing middleware sees them.
public sealed class EndpointConventionsTests
{
    public static TheoryData<string, Assembly> GetApiAssemblies() =>
        new()
        {
            { "Ordering", typeof(EndpointTags).Assembly },
            { "Billing", typeof(BillingDbContext).Assembly }
        };

    [Theory]
    [MemberData(nameof(GetApiAssemblies))]
    public void Endpoints_ShouldDeclareAuthorizationExplicitly(string contextName, Assembly apiAssembly)
    {
        IEnumerable<string> violations = GetEndpoints(contextName, apiAssembly)
            .Where(endpoint => !HasExplicitAuthorization(endpoint))
            .Select(Describe);

        violations.Should().BeEmpty($"{contextName} endpoints must call RequireAuthorization() or AllowAnonymous()");
    }

    [Theory]
    [MemberData(nameof(GetApiAssemblies))]
    public void Endpoints_ShouldDeclareNameAndTags(string contextName, Assembly apiAssembly)
    {
        List<string> violations = [];

        foreach (RouteEndpoint endpoint in GetEndpoints(contextName, apiAssembly))
        {
            if (string.IsNullOrWhiteSpace(endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName))
            {
                violations.Add($"{Describe(endpoint)} is missing WithName()");
            }

            if (endpoint.Metadata.GetMetadata<ITagsMetadata>() is not { Tags.Count: > 0 })
            {
                violations.Add($"{Describe(endpoint)} is missing WithTags()");
            }
        }

        violations.Should().BeEmpty($"{contextName} endpoints must be identifiable in the OpenAPI document");
    }

    [Theory]
    [MemberData(nameof(GetApiAssemblies))]
    public void Endpoints_ShouldDeclareSuccessAndProblemResponses(string contextName, Assembly apiAssembly)
    {
        List<string> violations = [];

        foreach (RouteEndpoint endpoint in GetEndpoints(contextName, apiAssembly))
        {
            IReadOnlyList<IProducesResponseTypeMetadata> responses =
                endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();

            if (!responses.Any(response => response.StatusCode is >= StatusCodes.Status200OK and < StatusCodes.Status300MultipleChoices))
            {
                violations.Add($"{Describe(endpoint)} is missing Produces() for its success response");
            }

            if (!responses.Any(IsProblemResponse))
            {
                violations.Add($"{Describe(endpoint)} is missing ProducesProblem()");
            }
        }

        violations.Should().BeEmpty($"{contextName} endpoints must document both success and failure responses");
    }

    private static IReadOnlyList<RouteEndpoint> GetEndpoints(string contextName, Assembly apiAssembly)
    {
        IReadOnlyList<RouteEndpoint> endpoints = ApiEndpointCatalog.GetRouteEndpoints(apiAssembly);

        endpoints.Should().NotBeEmpty($"{contextName} API must map at least one endpoint");

        return endpoints;
    }

    // RequireAuthorization(string[]) adds IAuthorizeData, RequireAuthorization(AuthorizationPolicy) adds the policy itself.
    private static bool HasExplicitAuthorization(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null ||
        endpoint.Metadata.GetMetadata<AuthorizationPolicy>() is not null ||
        endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;

    private static bool IsProblemResponse(IProducesResponseTypeMetadata response) =>
        response.StatusCode >= StatusCodes.Status400BadRequest &&
        (response.Type?.IsAssignableTo(typeof(ProblemDetails)) ?? false);

    private static string Describe(RouteEndpoint endpoint)
    {
        IReadOnlyList<string> methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];

        return $"{string.Join('|', methods)} {endpoint.RoutePattern.RawText}";
    }
}
