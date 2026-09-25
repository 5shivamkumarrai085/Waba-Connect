using Microsoft.AspNetCore.SignalR;
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

    public EventPublisherConsumer(
        InProcessEventPublisher publisher,
        IHubContext<CampaignHub> hub,
        IServiceScopeFactory scopeFactory,
        ILogger<EventPublisherConsumer> logger)
    {
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
            recipientAddress  = notification.RecipientAddress,
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

            newCampaignStatus = notification.NewCampaignStatus
        };

        // Invalidate dashboard memory cache so subsequent dashboard polls get immediate fresh data
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dashboardCache = scope.ServiceProvider.GetService<IDashboardCacheService>();
            dashboardCache?.InvalidateCache();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to invalidate dashboard cache after email event.");
        }

        // Broadcast to all connected clients (for list & dashboard views)
        await _hub.Clients.All.SendAsync("campaignEvent", payload);
    }
}

