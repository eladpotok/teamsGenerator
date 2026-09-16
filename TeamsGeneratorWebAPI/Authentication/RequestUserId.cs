using System.Security.Claims;

namespace TeamsGeneratorWebAPI.Authentication;

internal static class RequestUserId
{
    internal static string Resolve(ClaimsPrincipal user, string fallbackUserId)
    {
        if (user.Identity?.IsAuthenticated == true)
        {
            var authenticatedUserId =
                user.FindFirstValue("user_id") ??
                user.FindFirstValue(ClaimTypes.NameIdentifier) ??
                user.FindFirstValue("sub");

            if (!string.IsNullOrWhiteSpace(authenticatedUserId))
            {
                return authenticatedUserId;
            }
        }

        return fallbackUserId;
    }

    internal static string? ResolveOptional(
        ClaimsPrincipal user,
        string? fallbackUserId)
    {
        if (user.Identity?.IsAuthenticated == true)
        {
            return Resolve(user, fallbackUserId ?? string.Empty);
        }

        return fallbackUserId;
    }

    internal static string? ResolveVerifiedEmail(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var verifiedClaim = user.FindFirstValue("email_verified");
        if (!bool.TryParse(verifiedClaim, out var isVerified) || !isVerified)
        {
            return null;
        }

        var email =
            user.FindFirstValue("email") ??
            user.FindFirstValue(ClaimTypes.Email);
        return string.IsNullOrWhiteSpace(email)
            ? null
            : email.Trim().ToLowerInvariant();
    }
}
