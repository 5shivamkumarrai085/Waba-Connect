using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WhatsAppCampaignApi.Data;
using Microsoft.AspNetCore.SignalR;
using WhatsAppCampaignApi.Hubs;

namespace WhatsAppCampaignApi.Services.Realtime;

/// <summary>
/// Tells open inboxes that something changed, so they refresh the affected conversation instead
/// of polling for it. Best-effort: a failed push is logged and never fails the caller, because
/// the data is already saved and the inbox's fallback poll will still pick it up.
/// </summary>
public interface IInboxNotifier
{
    /// <summary>A new inbound or outbound message was stored in a conversation.</summary>
    Task MessageReceivedAsync(int conversationId, int? connectionId, CancellationToken ct = default);

    /// <summary>An existing message changed delivery state (sent, delivered, read, failed).</summary>
    Task MessageStatusChangedAsync(int conversationId, int messageId, string status, CancellationToken ct = default);

    /// <summary>A conversation's owner, status or SLA changed.</summary>
    Task ConversationUpdatedAsync(int conversationId, int? connectionId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class SignalRInboxNotifier : IInboxNotifier
{
    private readonly IHubContext<CampaignHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SignalRInboxNotifier> _logger;
    private readonly IDashboardNotifier _dashboard;

    public SignalRInboxNotifier(
        IHubContext<CampaignHub> hub,
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache,
        ILogger<SignalRInboxNotifier> logger,
        IDashboardNotifier dashboard)
    {
        _dashboard = dashboard;
        _hub = hub;
        _scopeFactory = scopeFactory;
        _cache = cache;
        _logger = logger;
    }

    public Task MessageReceivedAsync(int conversationId, int? connectionId, CancellationToken ct = default)
    {
        // Messages and replies are dashboard figures too.
        _dashboard.Changed();
        return SendAsync("inboxEvent", new { type = "messageReceived", conversationId, connectionId }, connectionId, ct);
    }

    public Task ConversationUpdatedAsync(int conversationId, int? connectionId, CancellationToken ct = default) =>
        SendAsync("inboxEvent", new { type = "conversationUpdated", conversationId, connectionId }, connectionId, ct);

    public async Task MessageStatusChangedAsync(int conversationId, int messageId, string status, CancellationToken ct = default) =>
        await SendAsync("inboxEvent", new { type = "messageStatus", conversationId, messageId, status },
            await ConnectionOfAsync(conversationId, ct), ct);

    /// <summary>A conversation's connection, cached: it never changes once the conversation exists.</summary>
    private async Task<int?> ConnectionOfAsync(int conversationId, CancellationToken ct)
    {
        var key = $"inbox:conversation-connection:{conversationId}";
        if (_cache.TryGetValue(key, out int? cached)) return cached;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var connectionId = await db.ChatConversations.AsNoTracking()
                .Where(c => c.Id == conversationId)
                .Select(c => c.ConnectionId)
                .FirstOrDefaultAsync(ct);

            _cache.Set(key, connectionId, TimeSpan.FromHours(1));
            return connectionId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not resolve the connection of conversation {ConversationId}.", conversationId);
            return null;
        }
    }

    private async Task SendAsync(string method, object payload, int? connectionId, CancellationToken ct)
    {
        try
        {
            // Unrestricted viewers, plus those scoped to this conversation's connection.
            var groups = connectionId is { } id
                ? new[] { CampaignHub.InboxGroup, CampaignHub.InboxGroupFor(id) }
                : new[] { CampaignHub.InboxGroup };
            await _hub.Clients.Groups(groups).SendAsync(method, payload, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Inbox notification {Method} could not be delivered.", method);
        }
    }
}
