using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

/// <summary>
/// Deletes chat history older than the configured retention period.
///
/// <para>
/// Follows the same shape as <see cref="TemplateSyncBackgroundService"/> — a
/// <see cref="BackgroundService"/> with a startup delay, a fixed interval and backoff on failure —
/// because that is how scheduled work is already done in this codebase.
/// </para>
/// <para>
/// The setting is read at the start of every run rather than at construction, so switching it off
/// stops the next sweep instead of requiring a restart. A run with the toggle off does nothing at
/// all: it does not look at the data, let alone delete any.
/// </para>
/// <para>
/// Deletion is by message age, not conversation age. A thread that has been quiet for a year but
/// received a message yesterday keeps that message and loses the older ones — which is what a
/// retention period means. A conversation is removed only once it has no messages left, so an
/// active thread is never destroyed by a sweep.
/// </para>
/// </summary>
public class ChatHistoryCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChatHistoryCleanupService> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _startupDelay;

    /// <summary>
    /// Messages removed per statement.
    ///
    /// The first sweep on an old account can match hundreds of thousands of rows, and one
    /// unbounded DELETE would hold locks on the chat tables for as long as it took. Batching keeps
    /// each statement short and lets the loop notice a shutdown between them.
    /// </summary>
    private const int DeleteBatchSize = 5_000;

    public ChatHistoryCleanupService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<ChatHistoryCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = TimeSpan.FromHours(configuration.GetValue("ChatHistoryCleanup:IntervalHours", 6));
        _startupDelay = TimeSpan.FromSeconds(configuration.GetValue("ChatHistoryCleanup:StartupDelaySeconds", 90));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Behind the migration check, the seeder and the template sync — none of this is urgent.
        try { await Task.Delay(_startupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Never let a sweep failure end the service. A database blip should cost one run,
                // not every future one.
                _logger.LogError(ex, "Chat history cleanup run failed; will retry at the next interval.");
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One sweep. Public in effect via the service; separated for readability.</summary>
    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var settings = scope.ServiceProvider.GetRequiredService<IOmniSettingsService>();

        // Read fresh each run. The settings cache is invalidated on save, so this picks up a change
        // made a minute ago without a restart.
        settings.InvalidateCache();

        if (!await settings.GetFlagAsync("autoClear.enabled"))
        {
            _logger.LogDebug("Auto Clear Chat History is off; skipping sweep.");
            return;
        }

        var retentionDays = await settings.GetNumberAsync("autoClear.retentionDays", 0);

        // The settings layer already refuses anything below 1, but this runs unattended against
        // customer data: a zero here would delete everything ever sent, so it is checked again
        // rather than trusted.
        if (retentionDays < 1)
        {
            _logger.LogWarning(
                "Auto Clear Chat History is on but the retention period is {Days} days; refusing to run.",
                retentionDays);
            return;
        }

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Announced before it acts. This job deletes customer data unattended, so "it ran, against
        // this cutoff" needs to be in the log whether or not it found anything -- otherwise the
        // only evidence it works is data going missing.
        _logger.LogInformation(
            "Auto Clear Chat History sweeping messages older than {Days} days (before {Cutoff:u}).",
            retentionDays, cutoff);

        var totalMessages = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            // Oldest first, in batches. ExecuteDeleteAsync issues a single DELETE rather than
            // loading rows into memory to remove them one by one.
            var batchIds = await dbContext.ChatMessages
                .Where(m => m.CreatedAt < cutoff)
                .OrderBy(m => m.Id)
                .Take(DeleteBatchSize)
                .Select(m => m.Id)
                .ToListAsync(stoppingToken);

            if (batchIds.Count == 0) break;

            var removed = await dbContext.ChatMessages
                .Where(m => batchIds.Contains(m.Id))
                .ExecuteDeleteAsync(stoppingToken);

            totalMessages += removed;

            if (removed < DeleteBatchSize) break;
        }

        // Conversations that are now empty. A conversation with any surviving message is left
        // alone — the thread is still live, it has simply lost its oldest turns.
        var emptyConversations = 0;

        if (totalMessages > 0)
        {
            emptyConversations = await dbContext.ChatConversations
                .Where(c => c.LastMessageAt != null
                            && c.LastMessageAt < cutoff
                            && !dbContext.ChatMessages.Any(m => m.ConversationId == c.Id))
                .ExecuteDeleteAsync(stoppingToken);
        }

        if (totalMessages > 0 || emptyConversations > 0)
        {
            _logger.LogInformation(
                "Auto Clear Chat History removed {Messages} message(s) and {Conversations} empty conversation(s) older than {Days} days (before {Cutoff:u}).",
                totalMessages, emptyConversations, retentionDays, cutoff);
        }
        else
        {
            _logger.LogInformation(
                "Auto Clear Chat History found nothing older than {Days} days.", retentionDays);
        }
    }
}
