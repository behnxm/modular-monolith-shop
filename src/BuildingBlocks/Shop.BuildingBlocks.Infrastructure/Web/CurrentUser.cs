using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Shop.BuildingBlocks.Abstractions;

namespace Shop.BuildingBlocks.Infrastructure.Web;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public string UserId =>
        User?.FindFirst("sub")?.Value
        ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? string.Empty;

    public string DisplayName =>
        User?.FindFirst("name")?.Value
        ?? User?.FindFirst(ClaimTypes.Name)?.Value
        ?? UserId;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public bool HasScope(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        if (User is null)
        {
            return false;
        }

        var scopeClaims = User.FindAll("scope").Concat(User.FindAll("scp"));
        foreach (var claim in scopeClaims)
        {
            var scopes = claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (scopes.Contains(scope, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
