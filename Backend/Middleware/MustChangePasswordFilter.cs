using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;

namespace WhatsAppCampaignApi.Middleware;

/// <summary>
/// Enforces "must change password" on the server. The flag used to be a claim the client was
/// trusted to act on, so any caller holding the token could skip the forced change and use the
/// whole API with a password an administrator (or the seed) had chosen.
/// </summary>
public sealed class MustChangePasswordFilter : IAsyncAuthorizationFilter
{
    /// <summary>What a user with a pending password change may still reach.</summary>
    private static readonly string[] AllowedPaths =
    [
        "/api/auth/me",
        "/api/auth/change-password",
        "/api/auth/logout",
        "/api/auth/refresh",
        "/api/auth/login"
    ];

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true) return Task.CompletedTask;

        var mustChange = string.Equals(user.FindFirst(AuthClaims.MustChangePassword)?.Value, "true", StringComparison.OrdinalIgnoreCase);
        if (!mustChange) return Task.CompletedTask;

        var path = context.HttpContext.Request.Path;
        if (AllowedPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase))) return Task.CompletedTask;

        context.Result = new ObjectResult(new ApiResponse
        {
            Success = false,
            Message = "You must change your password before continuing.",
            Errors = ["PASSWORD_CHANGE_REQUIRED"]
        })
        { StatusCode = StatusCodes.Status403Forbidden };

        return Task.CompletedTask;
    }
}
