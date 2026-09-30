using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Options;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Limits how fast one connection may send, across every running instance.
/// </summary>
public interface IEmailRateLimiter
{
    /// <summary>
    /// Tries to reserve <paramref name="count"/> sends. Returns how many were actually granted,
    /// which may be fewer than asked for — or zero. Callers reduce their batch accordingly rather
    /// than sending anyway.
    /// </summary>
    Task<int> TryReserveAsync(int connectionId, int count, CancellationToken ct = default);

    /// <summary>
    /// As <see cref="TryReserveAsync(int, int, CancellationToken)"/>, seeding a new bucket at
    /// <paramref name="defaultRatePerSecond"/> instead of the email default — the same per-connection
    /// bucket serves WhatsApp connections, whose provider allows a much higher rate.
    /// </summary>
    Task<int> TryReserveAsync(int connectionId, int count, double defaultRatePerSecond, CancellationToken ct = default);

    /// <summary>
    /// Returns tokens for sends that did not happen, so a suppressed or skipped recipient does
    /// not consume someone else's send allowance.
    /// </summary>
    Task ReleaseAsync(int connectionId, int count, CancellationToken ct = default);
}

/// <summary>
/// A token bucket whose state lives in PostgreSQL.
///
/// <para>
/// In the database rather than in memory, because an in-process limiter is only correct while
/// exactly one instance is running. Scale to three and the provider quietly receives three times
/// the configured rate — which gets a mail account throttled, and then reputation-damaged. Since
/// horizontal scaling is an explicit requirement here, a per-process limiter would have been
/// wrong by construction.
/// </para>
/// <para>
/// Reserving is a single conditional <c>UPDATE … RETURNING</c> that refills by elapsed time and
/// decrements in the same statement, so two workers cannot both observe the same spare capacity.
/// The cost is one round trip per batch, which is negligible beside the provider call it gates.
/// </para>
/// </summary>
public class PostgresEmailRateLimiter : IEmailRateLimiter
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly ILogger<PostgresEmailRateLimiter> _logger;

    public PostgresEmailRateLimiter(
        NpgsqlDataSource dataSource,
        IOptionsMonitor<EmailOptions> options,
        ILogger<PostgresEmailRateLimiter> logger)
    {
        _dataSource = dataSource;
        _options = options;
        _logger = logger;
    }

    public Task<int> TryReserveAsync(int connectionId, int count, CancellationToken ct = default) =>
        TryReserveAsync(connectionId, count, _options.CurrentValue.Dispatch.DefaultSendRatePerSecond, ct);

    /// <summary>Buckets known to exist, so the hot path skips the seeding INSERT.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> _knownBuckets = new();

    public async Task<int> TryReserveAsync(int connectionId, int count, double defaultRatePerSecond, CancellationToken ct = default)
    {
        if (count <= 0) return 0;

        await using var connection = await OpenAsync(ct);

        // The bucket row is created on demand so a connection configured before this feature
        // existed, or one whose quota row was pruned, still sends at the configured default
        // rather than failing. Once seen it is not re-checked on every reservation.
        if (!_knownBuckets.ContainsKey(connectionId))
        {
            await EnsureBucketAsync(connection, connectionId, defaultRatePerSecond, ct);
            _knownBuckets[connectionId] = true;
        }

        // Refill and decrement in one statement:
        //   * refilled  — tokens plus elapsed-time accrual, capped at capacity.
        //   * granted   — the smaller of what was asked for and what is available, floored at 0.
        //
        // LastRefillAt always advances to now(), so accrual is never double-counted by two
        // workers reserving in the same instant.
        const string sql = """
            WITH refilled AS (
                SELECT "ConnectionId",
                       LEAST(
                           "Capacity",
                           "Tokens" + GREATEST(0, EXTRACT(EPOCH FROM (now() - "LastRefillAt"))) * "RefillPerSecond"
                       ) AS available
                  FROM "EmailSendQuotas"
                 WHERE "ConnectionId" = @connection_id
                 FOR UPDATE
            )
            UPDATE "EmailSendQuotas" q
               SET "Tokens"      = r.available - FLOOR(LEAST(@count, GREATEST(0, r.available))),
                   "LastRefillAt" = now(),
                   "UpdatedAt"   = now()
              FROM refilled r
             WHERE q."ConnectionId" = r."ConnectionId"
            RETURNING FLOOR(LEAST(@count, GREATEST(0, r.available)))::int AS granted
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("connection_id", connectionId);
        command.Parameters.AddWithValue("count", (double)count);

        var result = await command.ExecuteScalarAsync(ct);
        var granted = result is null or DBNull ? 0 : Convert.ToInt32(result);

        if (granted < count)
        {
            _logger.LogDebug(
                "Rate limit on connection {ConnectionId}: granted {Granted} of {Requested} send slot(s).",
                connectionId, granted, count);
        }

        return granted;
    }

    public async Task ReleaseAsync(int connectionId, int count, CancellationToken ct = default)
    {
        if (count <= 0) return;

        // Capped at capacity so returning tokens can never create burst allowance that was never
        // earned.
        const string sql = """
            UPDATE "EmailSendQuotas"
               SET "Tokens"    = LEAST("Capacity", "Tokens" + @count),
                   "UpdatedAt" = now()
             WHERE "ConnectionId" = @connection_id
            """;

        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("connection_id", connectionId);
        command.Parameters.AddWithValue("count", (double)count);

        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task EnsureBucketAsync(NpgsqlConnection connection, int connectionId, double defaultRate, CancellationToken ct)
    {
        // ON CONFLICT DO NOTHING rather than a check-then-insert: two workers hitting an
        // unseeded connection at once would otherwise race on the primary key.
        const string sql = """
            INSERT INTO "EmailSendQuotas"
                ("ConnectionId", "Capacity", "RefillPerSecond", "Tokens", "LastRefillAt", "CreatedAt", "UpdatedAt")
            VALUES (@connection_id, @rate, @rate, @rate, now(), now(), now())
            ON CONFLICT ("ConnectionId") DO NOTHING
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("connection_id", connectionId);
        command.Parameters.AddWithValue("rate", Math.Max(1, defaultRate));

        await command.ExecuteNonQueryAsync(ct);
    }

    private ValueTask<NpgsqlConnection> OpenAsync(CancellationToken ct) => _dataSource.OpenConnectionAsync(ct);
}
