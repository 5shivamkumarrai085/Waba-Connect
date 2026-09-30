using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Hubs;

/// <summary>
/// The application's single real-time hub, for campaign progress and inbox activity.
///
/// <para>
/// Membership is decided on the server when the connection opens, from the caller's permissions:
/// <c>Campaign.View</c> joins <see cref="CampaignsGroup"/> and <c>Chat.View</c> joins
/// <see cref="InboxGroup"/>. Nothing is ever broadcast to every connection, and payloads carry
/// ids and counter deltas only — no names, addresses or message text — so a client that wants
/// detail re-reads it through the API, where the normal authorization applies.
/// </para>
///
/// <para>
/// Scaling: a single instance works as-is. With several instances, set <c>SignalR:Redis</c> to
/// enable the Redis backplane; nothing here changes.
/// </para>
/// </summary>
[Authorize]
public class CampaignHub : Hub
{
    /// <summary>Unrestricted viewers: every campaign's events.</summary>
    public const string CampaignsGroup = "campaigns";

    /// <summary>Unrestricted viewers: every conversation's events.</summary>
    public const string InboxGroup = "inbox";

    /// <summary>Open dashboards: receives a data-free "dashboardChanged" signal (see DashboardNotifier).</summary>
    public const string DashboardGroup = "dashboard";

    /// <summary>Viewers restricted to one connection (connection scoping).</summary>
    public static string CampaignsGroupFor(int connectionId) => $"campaigns:conn:{connectionId}";

    /// <summary>Viewers restricted to one connection (connection scoping).</summary>
    public static string InboxGroupFor(int connectionId) => $"inbox:conn:{connectionId}";

    private readonly IPermissionResolver _permissions;
    private readonly Services.Security.IAccessScope _accessScope;
    private readonly ILogger<CampaignHub> _logger;

    public CampaignHub(IPermissionResolver permissions, Services.Security.IAccessScope accessScope, ILogger<CampaignHub> logger)
    {
        _permissions = permissions;
        _accessScope = accessScope;
        _logger = logger;
    }

    /// <summary>
    /// Joins the groups this caller may receive. An unrestricted caller joins the global group; a
    /// caller limited by connection scoping joins one group per assigned connection, so events
    /// for other connections never reach them. Assignment changes apply on the next connect.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var scope = await ResolveScopeAsync();

        if (await HasPermissionAsync("Campaign.View"))
        {
            await JoinAsync(CampaignsGroup, CampaignsGroupFor, scope);
        }

        if (await HasPermissionAsync("Chat.View"))
        {
            await JoinAsync(InboxGroup, InboxGroupFor, scope);
        }

        // The signal carries no numbers, so one group for everyone who may see the dashboard; each
        // client then refetches its own connection-scoped summary.
        if (await HasPermissionAsync("Dashboard.View"))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, DashboardGroup);
        }

        await base.OnConnectedAsync();
    }

    private async Task JoinAsync(string allGroup, Func<int, string> perConnection, IReadOnlySet<int>? scope)
    {
        if (scope is null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, allGroup);
            return;
        }

        foreach (var connectionId in scope)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, perConnection(connectionId));
        }
    }

    private async Task<IReadOnlySet<int>?> ResolveScopeAsync()
    {
        var user = Context.User;
        if (user?.Identity?.IsAuthenticated != true) return new HashSet<int>();

        var isAdmin = bool.TryParse(user.FindFirst(AuthClaims.IsAdministrator)?.Value, out var admin) && admin;
        if (!int.TryParse(user.FindFirst(AuthClaims.UserId)?.Value, out var userId)) return new HashSet<int>();

        return await _accessScope.GetAllowedConnectionIdsForUserAsync(userId, isAdmin);
    }

    /// <summary>
    /// Kept for clients that still call it. Campaign events already reach every connection in
    /// <see cref="CampaignsGroup"/>, so this only checks the permission.
    /// </summary>
    public async Task JoinCampaign(int campaignId)
    {
        if (!await HasPermissionAsync("Campaign.View"))
        {
            throw new HubException("You do not have access to campaigns.");
        }

        _logger.LogDebug("SignalR client {ConnectionId} watching campaign {CampaignId}.", Context.ConnectionId, campaignId);
    }

    /// <summary>Counterpart of <see cref="JoinCampaign"/>; nothing to undo.</summary>
    public Task LeaveCampaign(int campaignId) => Task.CompletedTask;

    private async Task<bool> HasPermissionAsync(string permissionKey)
    {
        var user = Context.User;
        if (user?.Identity?.IsAuthenticated != true) return false;

        if (bool.TryParse(user.FindFirst(AuthClaims.IsAdministrator)?.Value, out var isAdmin) && isAdmin)
        {
            return true;
        }

        if (!int.TryParse(user.FindFirst(AuthClaims.UserId)?.Value, out var userId)) return false;

        var effective = await _permissions.GetEffectivePermissionsAsync(userId);
        return effective.Contains(permissionKey);
    }
}
