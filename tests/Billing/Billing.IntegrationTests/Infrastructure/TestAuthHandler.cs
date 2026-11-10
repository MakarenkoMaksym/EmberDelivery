using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace EmberDelivery.Billing.IntegrationTests.Infrastructure;

// Replaces Keycloak JWT validation: the user id travels in the Authorization header and roles in a custom header,
// so tests exercise the real authorization policies and ownership checks without a running identity provider.
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

        if (Request.Headers.TryGetValue(RolesHeaderName, out StringValues roles) && !StringValues.IsNullOrEmpty(roles))
        {
            foreach (string role in roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, SchemeName));

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
