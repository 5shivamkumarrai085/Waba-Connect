using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WhatsAppCampaignApi.Data;

namespace WhatsAppCampaignApi.Services;

/// <summary>
/// Proves the process can still reach its database, for the load balancer behind <c>GET /health</c>.
///
/// <para>
/// The check runs a trivial query rather than <c>CanConnectAsync</c>: a pooled connection can be
/// open and the database still refuse to answer, and reporting healthy in that state is exactly
/// the failure a health endpoint exists to catch. The database is a remote Neon instance, so a
/// round trip is the only honest test.
/// </para>
/// <para>
/// Its own context comes from the factory, not the request scope — this runs on an unauthenticated
/// endpoint with no user, and it must not share a change tracker with anything.
/// </para>
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    /// <summary>
    /// Past this the node is degraded even if the query eventually returns. A load balancer that
    /// waits indefinitely for a health probe cannot drain a stalled node, which is the one job.
    /// </summary>
    private static readonly TimeSpan SlowThreshold = TimeSpan.FromSeconds(3);

    public DatabaseHealthCheck(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
            stopwatch.Stop();

            var data = new Dictionary<string, object> { ["latencyMs"] = stopwatch.ElapsedMilliseconds };

            return stopwatch.Elapsed > SlowThreshold
                ? HealthCheckResult.Degraded(
                    $"Database responded in {stopwatch.ElapsedMilliseconds} ms.", data: data)
                : HealthCheckResult.Healthy(
                    $"Database responded in {stopwatch.ElapsedMilliseconds} ms.", data);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            // The exception is passed through rather than its message being pasted into the
            // response body — the endpoint is unauthenticated, and the writer decides what an
            // anonymous caller is told.
            return HealthCheckResult.Unhealthy("Database is unreachable.", ex);
        }
    }
}
