using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Options;

namespace WhatsAppCampaignApi.Services.Queue;

/// <summary>
/// Housekeeping for the job queue: prunes completed rows and reports depth.
///
/// <para>
/// Both halves matter operationally. Completed rows are kept for a while so an operator can
/// answer "did this job run, and when" after an incident, but kept forever they turn the queue
/// table into an archive whose indexes no longer fit in cache. And a queue with no depth metric
/// fails silently — nobody notices a stalled worker until customers report missing mail, which is
/// why the age of the oldest pending job is logged rather than just the count.
/// </para>
/// </summary>
public class JobQueueMaintenanceWorker : BackgroundService
{
    /// <summary>
    /// Long, because neither job is urgent, and a sweep that competes with the dispatch workers
    /// for connections costs throughput for no benefit.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    /// <summary>Deleted in slices so a large backlog cannot hold a long transaction open.</summary>
    private const int PruneBatchSize = 5000;

    private readonly IJobQueue _queue;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly ILogger<JobQueueMaintenanceWorker> _logger;

    public JobQueueMaintenanceWorker(
        IJobQueue queue,
        IDbContextFactory<AppDbContext> contextFactory,
        IOptionsMonitor<EmailOptions> options,
        ILogger<JobQueueMaintenanceWorker> logger)
    {
        _queue = queue;
        _contextFactory = contextFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the app finish starting before adding database work to the mix.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReportDepthAsync(stoppingToken);
                await PruneCompletedAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let housekeeping take the host down, and never let one bad sweep stop
                // all future ones.
                _logger.LogError(ex, "Job queue maintenance sweep failed.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
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

    private async Task PruneCompletedAsync(CancellationToken ct)
    {
        var retentionDays = _options.CurrentValue.Queue.CompletedRetentionDays;

        // Zero is a legitimate choice meaning "keep nothing", but it is also what an unbound
        // configuration section would produce, so it is treated as "not configured" instead of
        // silently deleting every completed row the moment it lands.
        if (retentionDays <= 0) return;

        const string sql = """
            DELETE FROM "JobQueue"
             WHERE "Id" IN (
                 SELECT "Id" FROM "JobQueue"
                  WHERE "Status" = 'Completed'
                    AND "CompletedAt" < now() - make_interval(days => @retention_days)
                  LIMIT @batch_size
             )
            """;

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var connectionString = context.Database.GetConnectionString();
        if (string.IsNullOrEmpty(connectionString)) return;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        var totalDeleted = 0;
        while (!ct.IsCancellationRequested)
        {
            await using var command = new NpgsqlCommand(sql, connection);
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
                "Pruned {Count} completed job(s) older than {RetentionDays} day(s).",
                totalDeleted, retentionDays);
        }
    }
}
