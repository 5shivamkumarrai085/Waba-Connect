using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace WhatsAppCampaignApi.Hubs;

/// <summary>
/// SignalR hub for real-time campaign email event notifications.
///
/// <para>
/// Clients join a group named <c>campaign:{campaignId}</c> to receive live updates while viewing
/// that campaign's detail page. When an email event is processed (sent, failed, bounced, opened,
/// clicked, replied, etc.), the backend publishes a notification to that group.
/// </para>
///
/// <para>
/// Authorization: JWT bearer is required. Only authenticated users may subscribe.
/// Future: add campaign-ownership check to prevent cross-tenant group access.
/// </para>
///
/// <para>
/// Scaling: in a single-instance deployment this works as-is. For horizontal scaling, add a
/// Redis backplane: <c>services.AddSignalR().AddStackExchangeRedis(connectionString)</c>.
/// No code changes needed beyond that registration.
/// </para>
/// </summary>
[Authorize]
public class CampaignHub : Hub
{
    private readonly ILogger<CampaignHub> _logger;

    public CampaignHub(ILogger<CampaignHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Called by the client to start receiving events for a specific campaign.
    /// </summary>
    public async Task JoinCampaign(int campaignId)
    {
        var groupName = CampaignGroupName(campaignId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        _logger.LogDebug(
            "SignalR client {ConnectionId} joined group {Group}.",
            Context.ConnectionId, groupName);
    }

    /// <summary>
    /// Called by the client when leaving the campaign page.
    /// </summary>
    public async Task LeaveCampaign(int campaignId)
    {
        var groupName = CampaignGroupName(campaignId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

        _logger.LogDebug(
            "SignalR client {ConnectionId} left group {Group}.",
            Context.ConnectionId, groupName);
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogDebug("SignalR client connected: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogDebug(
            "SignalR client disconnected: {ConnectionId} (reason: {Reason})",
            Context.ConnectionId, exception?.Message ?? "clean");
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// The group name convention: <c>campaign:{campaignId}</c>.
    /// Must match the string used in <see cref="EventPublisherConsumer"/>.
    /// </summary>
    public static string CampaignGroupName(int campaignId) => $"campaign:{campaignId}";
}
