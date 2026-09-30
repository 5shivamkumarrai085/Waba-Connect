using Microsoft.AspNetCore.SignalR;
using WhatsAppCampaignApi.Hubs;

namespace WhatsAppCampaignApi.Services.Realtime;

/// <summary>
/// Tells open dashboards that their numbers changed, so they refresh at once instead of waiting
/// for their next poll.
/// </summary>
/// <remarks>
/// Debounced: however many messages, replies or campaign events arrive, at most one
/// <c>dashboardChanged</c> signal goes out per interval (<c>Dashboard:LiveUpdateIntervalSeconds</c>),
/// and the last change in a burst is always followed by one. The signal carries no data — each
/// dashboard refetches its own (permission-scoped, server-cached) summary — so a campaign sending
/// thousands of messages a minute costs each open dashboard one refresh per interval, not one per
/// message. With several API instances, a SignalR backplane (Redis) is needed for the signal to
/// reach clients connected to the other instances; see FEATURE_GUIDE.md › Scaling.
/// </remarks>
public interface IDashboardNotifier
{
    /// <summary>Something the dashboard counts changed. Cheap and non-blocking; never throws.</summary>
    void Changed();
}

/// <inheritdoc />
public sealed class DashboardNotifier : IDashboardNotifier, IDisposable
{
    private readonly IHubContext<CampaignHub> _hub;
    private readonly ILogger<DashboardNotifier> _logger;
    private readonly TimeSpan _interval;
    private readonly Timer _timer;
    private int _scheduled;

    public DashboardNotifier(IHubContext<CampaignHub> hub, IConfiguration configuration, ILogger<DashboardNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Dashboard:LiveUpdateIntervalSeconds", 3), 1, 300));
        _timer = new Timer(_ => _ = FlushAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Changed()
    {
        // Only the first change in a window arms the timer; the rest ride along with it.
        if (Interlocked.CompareExchange(ref _scheduled, 1, 0) == 0)
        {
            _timer.Change(_interval, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task FlushAsync()
    {
        Interlocked.Exchange(ref _scheduled, 0);
        try
        {
            await _hub.Clients.Group(CampaignHub.DashboardGroup).SendAsync("dashboardChanged", new { at = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            // Best effort: the dashboard's fallback poll still picks the change up.
            _logger.LogDebug(ex, "Could not signal dashboards.");
        }
    }

    public void Dispose() => _timer.Dispose();
}
