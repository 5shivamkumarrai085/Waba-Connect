using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services.Email;

namespace WhatsAppCampaignApi.Services.Queue;

/// <summary>
/// Housekeeping for the job queue and the campaigns it drives.
///
/// <para>
/// Two cadences. Every minute: dead-letter jobs whose final attempt lost its lease, finish email
/// campaigns that have nothing left pending (including ones where every recipient was skipped, which
/// no send event ever finalises), and reconcile active campaigns' KPI counters against their
/// recipient rows. Every ten minutes: log queue depth and age, and prune old completed and
/// dead-lettered rows so the table's indexes stay in cache.
/// </para>
///
/// <para>
/// Every step is idempotent and safe on any number of instances at once.
/// </para>
/// </summary>
public class JobQueueMaintenanceWorker : BackgroundService
{
    private static readonly TimeSpan FastInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SlowInterval = TimeSpan.FromMinutes(10);

    /// <summary>Deleted in slices so a large backlog cannot hold a long transaction open.</summary>
    private const int PruneBatchSize = 5000;

    /// <summary>Recently finished campaigns keep being reconciled for this long, because opens,
    /// clicks and replies go on arriving after the last send.</summary>
    private static readonly TimeSpan ReconcileWindow = TimeSpan.FromHours(6);

    private readonly IJobQueue _queue;
    private readonly NpgsqlDataSource _dataSource;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly ILogger<JobQueueMaintenanceWorker> _logger;

    public JobQueueMaintenanceWorker(
        IJobQueue queue,
        NpgsqlDataSource dataSource,
        IDbContextFactory<AppDbContext> contextFactory,
        IOptionsMonitor<EmailOptions> options,
        ILogger<JobQueueMaintenanceWorker> logger)
    {
        _queue = queue;
        _dataSource = dataSource;
        _contextFactory = contextFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastSlowSweep = DateTime.MinValue;

        try
        {
            // Let the app finish starting before adding database work to the mix.
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await RunStepAsync("dead-letter exhausted leases", () => _queue.DeadLetterExhaustedLeasesAsync(stoppingToken), stoppingToken);
                await RunStepAsync("campaign sweep", () => SweepCampaignsAsync(stoppingToken), stoppingToken);

                if (DateTime.UtcNow - lastSlowSweep >= SlowInterval)
                {
                    await RunStepAsync("queue depth report", () => ReportDepthAsync(stoppingToken), stoppingToken);
                    await RunStepAsync("prune completed", () => PruneAsync("Completed", "CompletedAt", _options.CurrentValue.Queue.CompletedRetentionDays, stoppingToken), stoppingToken);
                    await RunStepAsync("prune dead-lettered", () => PruneAsync("DeadLettered", "DeadLetteredAt", _options.CurrentValue.Queue.DeadLetterRetentionDays, stoppingToken), stoppingToken);
                    lastSlowSweep = DateTime.UtcNow;
                }

                await Task.Delay(FastInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    /// <summary>Never lets one failing step stop the others or take the host down.</summary>
    private async Task RunStepAsync(string name, Func<Task> step, CancellationToken ct)
    {
        try
        {
            await step();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job queue maintenance step '{Step}' failed.", name);
        }
    }

    /// <summary>
    /// Finalises campaigns with nothing pending (either channel) and reconciles email counters for
    /// active and recently finished email campaigns.
    /// </summary>
    /// <summary>
    /// Campaigns whose counters the sweep recomputes: everything still sending, and email campaigns
    /// touched or sent an event since <paramref name="since"/>. The second half matters for
    /// finished campaigns: an open arriving days later records an event even if the processor
    /// died before it could stamp the recipient, and only the sweep can then repair it.
    /// </summary>
    public static async Task<IReadOnlyList<int>> CampaignsToReconcileAsync(AppDbContext db, DateTime since, CancellationToken ct)
    {
        var withRecentEvents = db.EmailEvents
            .Where(e => e.CreatedAt >= since && e.CampaignId != null)
            .Select(e => e.CampaignId!.Value);
        return await db.Campaigns
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => !c.IsDeleted
                     && (c.Status == CampaignStatus.Sending
                         || (c.Channel == MessageChannel.Email && (c.UpdatedAt >= since || withRecentEvents.Contains(c.Id)))))
            .OrderBy(c => c.Id)
            .Select(c => c.Id)
            .Take(500)
            .ToListAsync(ct);
    }

    private async Task SweepCampaignsAsync(CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var since = DateTime.UtcNow - ReconcileWindow;

        var ids = await CampaignsToReconcileAsync(db, since, ct);
        var campaigns = await db.Campaigns
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.Status, c.Channel })
            .ToListAsync(ct);

        foreach (var campaign in campaigns)
        {
            if (ct.IsCancellationRequested) break;

            // Counters are reconciled for email only: WhatsApp counters are moved by exact
            // per-transition deltas in the status webhook and the send worker.
            if (campaign.Channel == MessageChannel.Email)
            {
                await CampaignFinalizer.ReconcileEmailCountersAsync(db, campaign.Id, ct);
            }

            if (campaign.Status == CampaignStatus.Sending)
            {
                var status = await CampaignFinalizer.TryFinalizeAsync(db, campaign.Id, ct);
                if (status is not null)
                {
                    _logger.LogInformation("Campaign {CampaignId} finished as {Status} (maintenance sweep).", campaign.Id, status);
                }
            }
        }
    }

    private async Task ReportDepthAsync(CancellationToken ct)
    {
        foreach (var queueName in QueueNames.All)
        {
            var snapshot = await _queue.GetDepthAsync(queueName, ct);

            // Nothing queued and nothing stuck is the normal state; logging it every ten minutes
            // for every queue would bury the cases that matter.
            if (snapshot.Pending == 0 && snapshot.Leased == 0 && snapshot.DeadLettered == 0) continue;

            var oldestAgeMinutes = snapshot.OldestPendingAt is { } oldest
                ? Math.Round((DateTime.UtcNow - oldest).TotalMinutes, 1)
                : 0;

            _logger.LogInformation(
                "Queue {Queue}: {Pending} pending, {Leased} leased, {DeadLettered} dead-lettered, "
              + "{ExpiredLeases} expired lease(s), oldest pending {OldestAgeMinutes} min.",
                queueName, snapshot.Pending, snapshot.Leased, snapshot.DeadLettered,
                snapshot.ExpiredLeases, oldestAgeMinutes);

            if (snapshot.DeadLettered > 0)
            {
                _logger.LogWarning(
                    "Queue {Queue} has {Count} dead-lettered job(s) awaiting operator attention.",
                    queueName, snapshot.DeadLettered);
            }

            // Repeatedly non-zero means workers are being killed mid-job, or the visibility
            // timeout is shorter than a real send takes. Either way it points at duplicate sends.
            if (snapshot.ExpiredLeases > 0)
            {
                _logger.LogWarning(
                    "Queue {Queue} has {Count} expired lease(s). If this persists, workers are failing "
                  + "mid-job or Email:Queue:VisibilityTimeoutSeconds is too short.",
                    queueName, snapshot.ExpiredLeases);
            }
        }
    }

    private async Task PruneAsync(string status, string timestampColumn, int retentionDays, CancellationToken ct)
    {
        // Zero is a legitimate choice meaning "keep nothing", but it is also what an unbound
        // configuration section would produce, so it is treated as "not configured" instead of
        // silently deleting every row the moment it lands.
        if (retentionDays <= 0) return;

        // status and timestampColumn are compile-time constants from this class, never input.
        var sql = $"""
            DELETE FROM "JobQueue"
             WHERE "Id" IN (
                 SELECT "Id" FROM "JobQueue"
                  WHERE "Status" = @status
                    AND "{timestampColumn}" < now() - make_interval(days => @retention_days)
                  LIMIT @batch_size
             )
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(ct);

        var totalDeleted = 0;
        while (!ct.IsCancellationRequested)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("status", status);
            command.Parameters.AddWithValue("retention_days", retentionDays);
            command.Parameters.AddWithValue("batch_size", PruneBatchSize);

            var deleted = await command.ExecuteNonQueryAsync(ct);
            totalDeleted += deleted;

            // A short batch means the backlog is drained; stop rather than spinning.
            if (deleted < PruneBatchSize) break;
        }

        if (totalDeleted > 0)
        {
            _logger.LogInformation(
                "Pruned {Count} {Status} job(s) older than {RetentionDays} day(s).",
                totalDeleted, status, retentionDays);
        }
    }
}
