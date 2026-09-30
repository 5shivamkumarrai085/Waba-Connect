using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WhatsAppCampaignApi.Data;

namespace WhatsAppCampaignApi.Services.Queue;

/// <summary>
/// Reports the job queue as degraded when work is piling up or failing, so a stalled worker is
/// visible to monitoring before customers notice missing messages.
/// </summary>
/// <remarks>
/// Degraded, never Unhealthy: a backlog is a reason to alert, not to pull the node out of the load
/// balancer — the API itself still serves requests fine.
/// </remarks>
public sealed class QueueHealthCheck : IHealthCheck
{
    private static readonly TimeSpan MaxPendingAge = TimeSpan.FromMinutes(15);

    /// <summary>Probes arrive every few seconds; the queue scan runs at most this often.</summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);
    private static (DateTime At, HealthCheckResult Result)? _last;

    private readonly IJobQueue _queue;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public QueueHealthCheck(IJobQueue queue, IDbContextFactory<AppDbContext> dbFactory)
    {
        _queue = queue;
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Database sessions polling the job queue that were not opened by this build (their
    /// application name differs). They are usually a teammate's checkout or an old deployment
    /// pointed at the same database; before queue names carried a version they took jobs they
    /// could not process. Best effort: returns 0 when the server hides other sessions.
    /// </summary>
    public static async Task<int> CountForeignQueueClientsAsync(AppDbContext db, CancellationToken ct)
    {
        try
        {
            return await db.Database.SqlQueryRaw<int>("""
                SELECT count(*)::int AS "Value"
                FROM pg_stat_activity
                WHERE datname = current_database()
                  AND pid <> pg_backend_pid()
                  AND application_name IS DISTINCT FROM current_setting('application_name')
                  AND query ILIKE '%"JobQueue"%'
                """).SingleAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return 0;
        }
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_last is { } cached && DateTime.UtcNow - cached.At < CacheFor) return cached.Result;

        var result = await EvaluateAsync(cancellationToken);
        _last = (DateTime.UtcNow, result);
        return result;
    }

    private async Task<HealthCheckResult> EvaluateAsync(CancellationToken cancellationToken)
    {
        var problems = new List<string>();

        foreach (var queueName in QueueNames.All)
        {
            var depth = await _queue.GetDepthAsync(queueName, cancellationToken);

            if (depth.OldestPendingAt is { } oldest && DateTime.UtcNow - oldest > MaxPendingAge)
            {
                problems.Add($"{queueName}: oldest ready job waiting {(int)(DateTime.UtcNow - oldest).TotalMinutes} min");
            }

            if (depth.DeadLettered > 0)
            {
                problems.Add($"{queueName}: {depth.DeadLettered} dead-lettered");
            }
        }

        await using (var db = await _dbFactory.CreateDbContextAsync(cancellationToken))
        {
            var foreign = await CountForeignQueueClientsAsync(db, cancellationToken);
            if (foreign > 0)
            {
                problems.Add($"{foreign} database session(s) from another build of the app are polling the job queue; "
                    + "stop or update that instance (it cannot take this build's jobs, but it still runs its own)");
            }
        }

        return problems.Count == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded(string.Join("; ", problems));
    }
}
