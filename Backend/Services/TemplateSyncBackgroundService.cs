using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

/// <summary>
/// Keeps the local Templates table in step with Meta on a schedule.
///
/// <para>
/// The template list used to sync from Meta on every page load, which meant one outbound HTTPS
/// call per connected WABA — plus a database write — before the page could render a single row.
/// Loading Templates took 7-15 seconds and a Meta outage hung the page rather than showing the
/// rows already stored locally.
/// </para>
/// <para>
/// Reads are now served entirely from the database. This service refreshes in the background,
/// and <c>POST /api/Templates/sync</c> (permission <c>Template.LoadTemplate</c>) still forces an
/// immediate refresh for anyone who needs Meta's current state right now.
/// </para>
/// </summary>
public class TemplateSyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TemplateSyncBackgroundService> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _startupDelay;
    private readonly TimeSpan _timeout;

    /// <summary>Backoff after a failed run, capped at the normal interval.</summary>
    private TimeSpan _currentBackoff = TimeSpan.Zero;

    public TemplateSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<TemplateSyncBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = TimeSpan.FromMinutes(configuration.GetValue("TemplateSync:IntervalMinutes", 15));
        _startupDelay = TimeSpan.FromSeconds(configuration.GetValue("TemplateSync:StartupDelaySeconds", 30));
        _timeout = TimeSpan.FromSeconds(configuration.GetValue("TemplateSync:TimeoutSeconds", 60));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Don't compete with the migration check and seeder during startup.
        try { await Task.Delay(_startupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var succeeded = await RunOnceAsync(stoppingToken);

            // Exponential backoff on failure so a Meta outage doesn't turn into a retry storm,
            // reset the moment a run succeeds.
            if (succeeded)
            {
                _currentBackoff = TimeSpan.Zero;
            }
            else
            {
                _currentBackoff = _currentBackoff == TimeSpan.Zero
                    ? TimeSpan.FromMinutes(1)
                    : TimeSpan.FromTicks(Math.Min(_currentBackoff.Ticks * 2, _interval.Ticks));
            }

            var delay = _currentBackoff == TimeSpan.Zero ? _interval : _currentBackoff;

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<bool> RunOnceAsync(CancellationToken stoppingToken)
    {
        // A time-boxed run: a hung Meta call must not stall the loop indefinitely.
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeoutSource.CancelAfter(_timeout);

        try
        {
            // Its own scope per iteration — the scoped AppDbContext belongs to a request, and
            // holding one for the process lifetime would leak a connection.
            using var scope = _scopeFactory.CreateScope();
            var templateService = scope.ServiceProvider.GetRequiredService<ITemplateService>();

            var added = await templateService.SyncFromWhatsAppAsync();

            if (added > 0)
            {
                _logger.LogInformation("Template sync added {Count} new template(s) from Meta.", added);
            }

            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return true; // Shutting down, not a failure.
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Template sync timed out after {Seconds}s; will retry with backoff.", _timeout.TotalSeconds);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Template sync failed; will retry with backoff.");
            return false;
        }
    }
}
