using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WhatsAppCampaignApi.Hubs;
using WhatsAppCampaignApi.Services.Email;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Background consumer that reads from <see cref="InProcessEventPublisher"/> and forwards
/// notifications to SignalR.
/// </summary>
public class EventPublisherConsumer : BackgroundService
{
    private readonly InProcessEventPublisher _publisher;
    private readonly IHubContext<CampaignHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventPublisherConsumer> _logger;
    private readonly IMemoryCache _cache;
    private readonly WhatsAppCampaignApi.Services.Integrations.IWebhookEmitter _webhooks;

    public EventPublisherConsumer(
        InProcessEventPublisher publisher,
        IHubContext<CampaignHub> hub,
        IServiceScopeFactory scopeFactory,
        ILogger<EventPublisherConsumer> logger,
        IMemoryCache cache,
        WhatsAppCampaignApi.Services.Integrations.IWebhookEmitter webhooks)
    {
        _cache = cache;
        _webhooks = webhooks;
        _publisher    = publisher;
        _hub          = hub;
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Campaign event consumer started.");

        await foreach (var notification in _publisher.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ForwardToSignalRAsync(notification);
            }
            catch (Exception ex)
            {
                // A failed SignalR push must never crash the consumer loop.
                // The worst case is that a client misses one real-time update.
                _logger.LogWarning(ex,
                    "Failed to forward email event {Kind} for campaign {CampaignId} to SignalR.",
                    notification.Kind, notification.CampaignId);
            }

            try
            {
                await EmitWebhooksAsync(notification, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to raise webhooks for campaign {CampaignId}.", notification.CampaignId);
            }
        }

        _logger.LogInformation("Campaign event consumer stopped.");
    }

    private async Task ForwardToSignalRAsync(CampaignEmailEventNotification notification)
    {
        var payload = new
        {
            campaignId        = notification.CampaignId,
            kind              = notification.Kind.ToString(),
            campaignContactId = notification.CampaignContactId,
            occurredAt        = notification.OccurredAt,

            // Counter deltas — the client applies these to its local state without a reload
            sentDelta         = notification.SentDelta,
            failedDelta       = notification.FailedDelta,
            deliveredDelta    = notification.DeliveredDelta,
            bouncedDelta      = notification.BouncedDelta,
            openedDelta       = notification.OpenedDelta,
            clickedDelta      = notification.ClickedDelta,
            repliedDelta      = notification.RepliedDelta,
            unsubscribedDelta = notification.UnsubscribedDelta,
            complainedDelta   = notification.ComplainedDelta,
            readDelta         = notification.ReadDelta,

            newCampaignStatus = notification.NewCampaignStatus
        };

        // Invalidate dashboard memory cache so subsequent dashboard polls get immediate fresh data
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dashboardCache = scope.ServiceProvider.GetService<IDashboardCacheService>();
            dashboardCache?.InvalidateCache();
            scope.ServiceProvider.GetService<Realtime.IDashboardNotifier>()?.Changed();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to invalidate dashboard cache after email event.");
        }

        // Unrestricted campaign viewers, plus those scoped to this campaign's connection (see
        // CampaignHub). The payload deliberately carries no recipient address.
        var connectionId = await ConnectionOfAsync(notification.CampaignId);
        var groups = connectionId is { } id
            ? new[] { CampaignHub.CampaignsGroup, CampaignHub.CampaignsGroupFor(id) }
            : new[] { CampaignHub.CampaignsGroup };
        await _hub.Clients.Groups(groups).SendAsync("campaignEvent", payload);
    }

    /// <summary>
    /// The same event, for outbound webhooks: one event per counter that moved (WhatsApp status
    /// batches can move several at once, hence <c>count</c>), plus a status change if there was one.
    /// </summary>
    private async Task EmitWebhooksAsync(CampaignEmailEventNotification n, CancellationToken ct)
    {
        var events = new List<(string Type, int Count)>();
        void Add(string type, int delta) { if (delta > 0) events.Add((type, delta)); }
        Add("message.sent", n.SentDelta);
        Add("message.delivered", n.DeliveredDelta);
        Add("message.read", n.ReadDelta);
        Add("message.failed", n.FailedDelta);
        Add("email.opened", n.OpenedDelta);
        Add("email.clicked", n.ClickedDelta);
        Add("email.replied", n.RepliedDelta);
        Add("email.bounced", n.BouncedDelta);
        Add("email.unsubscribed", n.UnsubscribedDelta);
        Add("email.complained", n.ComplainedDelta);
        if (events.Count == 0 && n.NewCampaignStatus is null) return;

        var connectionId = await ConnectionOfAsync(n.CampaignId);
        IReadOnlyDictionary<string, object?>? personal = n.RecipientAddress is null
            ? null
            : new Dictionary<string, object?> { ["recipient"] = n.RecipientAddress };

        foreach (var (type, count) in events)
        {
            await _webhooks.EmitAsync(type, connectionId, new Dictionary<string, object?>
            {
                ["campaignId"] = n.CampaignId,
                ["campaignContactId"] = n.CampaignContactId,
                ["count"] = count,
                ["occurredAt"] = n.OccurredAt
            }, personal, ct);
        }

        if (n.NewCampaignStatus is not null)
        {
            await _webhooks.EmitAsync("campaign.status_changed", connectionId, new Dictionary<string, object?>
            {
                ["campaignId"] = n.CampaignId,
                ["status"] = n.NewCampaignStatus
            }, null, ct);
        }
    }

    /// <summary>A campaign's connection, cached: events for one campaign arrive in bursts.</summary>
    private async Task<int?> ConnectionOfAsync(int campaignId)
    {
        var key = $"campaign-connection:{campaignId}";
        if (_cache.TryGetValue(key, out int? cached)) return cached;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WhatsAppCampaignApi.Data.AppDbContext>();
        var connectionId = await db.Campaigns.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Id == campaignId)
            .Select(c => c.ConnectionId)
            .FirstOrDefaultAsync();

        _cache.Set(key, connectionId, TimeSpan.FromMinutes(10));
        return connectionId;
    }
}

