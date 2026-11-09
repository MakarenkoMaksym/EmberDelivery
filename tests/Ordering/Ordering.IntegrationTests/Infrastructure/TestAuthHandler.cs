using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace EmberDelivery.Ordering.IntegrationTests.Infrastructure;

public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestAuth";
    public const string RolesHeaderName = "X-Test-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string authHeader = Request.Headers.Authorization.ToString();

        if (!authHeader.StartsWith($"{SchemeName} ", StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string rawUserId = authHeader[$"{SchemeName} ".Length..].Trim();

        List<Claim> claims =
        [
            new Claim("sub", rawUserId),
            new Claim(ClaimTypes.NameIdentifier, rawUserId)
        ];

        if (Request.Headers.TryGetValue(RolesHeaderName, out StringValues customRoles) &&
            !StringValues.IsNullOrEmpty(customRoles))
        {
            foreach (string role in customRoles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }
        else
        {
            // Default roles matching Keycloak policies configured in AuthenticationExtensions.
            claims.Add(new Claim(ClaimTypes.Role, TestRoles.Customer));
            claims.Add(new Claim(ClaimTypes.Role, TestRoles.Admin));
        }

        ClaimsIdentity identity = new(claims, SchemeName);
        ClaimsPrincipal principal = new(identity);
        AuthenticationTicket ticket = new(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
