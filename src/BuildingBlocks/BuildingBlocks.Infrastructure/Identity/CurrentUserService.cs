using System.Security.Claims;

using EmberDelivery.BuildingBlocks.Application.Abstractions.Identity;

using Microsoft.AspNetCore.Http;

namespace EmberDelivery.BuildingBlocks.Infrastructure.Identity;

public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public Guid GetUserId()
    {
        string? rawId = GetRawIdentityId();

        if (Guid.TryParse(rawId, out Guid parsedUserId))
        {
            return parsedUserId;
        }

        throw new UnauthorizedAccessException("User is not authenticated or subject claim is not a valid UUID.");
    }

    public bool IsAuthenticated => Guid.TryParse(GetRawIdentityId(), out _);

    public bool IsInRole(string role) =>
        httpContextAccessor.HttpContext?.User?.IsInRole(role) ?? false;

    private string? GetRawIdentityId()
    {
        ClaimsPrincipal? user = httpContextAccessor.HttpContext?.User;

        // Check standard OIDC 'sub' claim first (used when MapInboundClaims = false), then fallback to NameIdentifier.
        return user?.FindFirst("sub")?.Value
            ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }
}
