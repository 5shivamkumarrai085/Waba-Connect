using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;

namespace WhatsAppCampaignApi.Middleware;

/// <summary>
/// Gates the controllers that predate authentication, without editing any of them.
///
/// <para>
/// The app shipped with every endpoint open. Adding [Authorize] to all 17 existing controllers
/// at once would break every page until the frontend auth wiring was complete, and any gap in
/// that wiring would leave a dead page. This filter instead applies globally and stays inert
/// until <c>Auth:EnforceOnLegacyEndpoints</c> is turned on, so the switch is one config value
/// and is reversible.
/// </para>
/// <para>
/// New Setup endpoints are unaffected either way — they carry their own
/// <see cref="RequiresPermissionAttribute"/> and enforce from day one.
/// </para>
/// </summary>
public class LegacyAuthGateFilter : IAsyncAuthorizationFilter
{
    private readonly IConfiguration _configuration;

    public LegacyAuthGateFilter(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var metadata = context.ActionDescriptor.EndpointMetadata;

        // Explicitly public. The Meta webhook lives here — it is called by Facebook's servers,
        // which will never hold a token, so gating it would silently stop all inbound messages.
        if (metadata.OfType<IAllowAnonymous>().Any())
            return Task.CompletedTask;

        // Endpoints that already declare their own permission requirement enforce themselves.
        if (context.Filters.Any(f => f is RequiresPermissionFilter))
            return Task.CompletedTask;

        var enforce = _configuration.GetValue<bool>("Auth:EnforceOnLegacyEndpoints");
        if (!enforce)
            return Task.CompletedTask;

        if (context.HttpContext.User?.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedObjectResult(new ApiResponse
            {
                Success = false,
                Message = "Authentication is required."
            });
        }

        return Task.CompletedTask;
    }
}
