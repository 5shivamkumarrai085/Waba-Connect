using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using WhatsAppCampaignApi.Models.Options;

namespace WhatsAppCampaignApi.Services.Queue;

/// <summary>
/// <see cref="IJobQueue"/> on PostgreSQL, using <c>FOR UPDATE SKIP LOCKED</c>.
///
/// <para>
/// Every statement here is hand-written rather than going through EF. That is not an optimisation
/// — it is a correctness requirement. Claiming work means "find ready rows, mark them mine and
/// set a lease, atomically, while other instances do the same", and the change tracker cannot
/// express that without a read-then-write race. <c>SKIP LOCKED</c> is what makes concurrent
/// claims scale: instead of blocking on rows a sibling is already claiming, a worker steps over
/// them and takes the next ones.
/// </para>
/// <para>
/// Each call opens its own short-lived connection from the context factory rather than borrowing
/// the request-scoped DbContext. Enqueueing frequently happens inside a request whose transaction
/// has not committed yet, and a job visible to a worker before the row it refers to exists is a
/// race that is very hard to diagnose. Owning the connection keeps the two independent, and keeps
/// this off the audit-change buffer attached to the scoped context.
/// </para>
/// </summary>
public class PostgresJobQueue : IJobQueue
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly ILogger<PostgresJobQueue> _logger;

    public PostgresJobQueue(
        NpgsqlDataSource dataSource,
        IOptionsMonitor<EmailOptions> options,
        ILogger<PostgresJobQueue> logger)
    {
        _dataSource = dataSource;
        _options = options;
        _logger = logger;
    }

    public string TransportName => "Postgres";

    private QueueOptions Queue => _options.CurrentValue.Queue;

    // A pooled connection from the process-wide data source. Raw Npgsql keeps these statements
    // clear of EF's command interception and of the scoped context's change tracker; the shared
    // pool means a claim no longer builds a DbContext just to learn the connection string.
    private ValueTask<NpgsqlConnection> OpenAsync(CancellationToken ct) => _dataSource.OpenConnectionAsync(ct);

    public async Task<QueueEnqueueResult> EnqueueAsync(
        IReadOnlyCollection<QueueMessage> messages,
        CancellationToken ct = default)
    {
        if (messages.Count == 0) return new QueueEnqueueResult(0, 0);

        var defaultMaxAttempts = Queue.MaxAttempts;
        var list = messages.ToList();

        // One set-based insert via unnest, rather than a statement per message: a campaign
        // expansion enqueues these in batches of a hundred, and a hundred round trips per batch
        // would dominate the expansion cost.
        //
        // COALESCE(..., now()) is load-bearing. AvailableAt is compared against the database's
        // now() when claiming, so defaulting it from the application clock makes correctness
        // depend on the app server and the database agreeing to the second. They do not — the
        // database is remote, and a clock a second fast silently makes every job briefly
        // unclaimable while a clock a second slow releases scheduled work early. Letting the
        // database stamp "immediately" keeps both sides of the comparison on one clock.
        const string sql = """
            INSERT INTO "JobQueue"
                ("QueueName", "PartitionKey", "Payload", "IdempotencyKey", "Status",
                 "Priority", "Attempt", "MaxAttempts", "AvailableAt", "CreatedAt", "UpdatedAt")
            SELECT t.queue, t.partition, t.payload::jsonb, t.idempotency, 'Pending',
                   t.priority, 0, t.max_attempts, COALESCE(t.available_at, now()), now(), now()
              FROM unnest(
                       @queues::text[], @partitions::text[], @payloads::text[],
                       @idempotency::text[], @priorities::int[], @max_attempts::int[],
                       @available::timestamptz[]
                   ) AS t(queue, partition, payload, idempotency, priority, max_attempts, available_at)
            ON CONFLICT ("QueueName", "IdempotencyKey") WHERE "IdempotencyKey" IS NOT NULL
            DO NOTHING
            """;

        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.Add(new NpgsqlParameter("queues", NpgsqlDbType.Array | NpgsqlDbType.Text)
        { Value = list.Select(m => m.QueueName).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("partitions", NpgsqlDbType.Array | NpgsqlDbType.Text)
        { Value = list.Select(m => (object?)m.PartitionKey ?? DBNull.Value).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("payloads", NpgsqlDbType.Array | NpgsqlDbType.Text)
        { Value = list.Select(m => m.Payload).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("idempotency", NpgsqlDbType.Array | NpgsqlDbType.Text)
        { Value = list.Select(m => (object?)m.IdempotencyKey ?? DBNull.Value).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("priorities", NpgsqlDbType.Array | NpgsqlDbType.Integer)
        { Value = list.Select(m => m.Priority).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("max_attempts", NpgsqlDbType.Array | NpgsqlDbType.Integer)
        { Value = list.Select(m => m.MaxAttempts ?? defaultMaxAttempts).ToArray() });
        // Null means "immediately", which the statement resolves with the database's own clock.
        command.Parameters.Add(new NpgsqlParameter("available", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz)
        {
            Value = list
                .Select(m => m.AvailableAt is { } at
                    ? (object)DateTime.SpecifyKind(at, DateTimeKind.Utc)
                    : DBNull.Value)
                .ToArray()
        });

        var inserted = await command.ExecuteNonQueryAsync(ct);
        var duplicates = list.Count - inserted;

        if (duplicates > 0)
        {
            // Expected and healthy on a re-run; noteworthy on a first run.
            _logger.LogDebug(
                "Enqueued {Inserted} job(s), skipped {Duplicates} already-queued idempotency key(s).",
                inserted, duplicates);
        }

        return new QueueEnqueueResult(inserted, duplicates);
    }

    public async Task<IReadOnlyList<QueueLease>> ClaimAsync(
        QueueClaimRequest request,
        CancellationToken ct = default)
    {
        var options = Queue;
        var batchSize = Math.Max(1, Math.Min(request.BatchSize, options.ClaimBatchSize));
        var perPartitionCap = Math.Max(1, request.PerPartitionCap ?? options.PerPartitionCap);
        var visibility = request.VisibilityTimeout ?? TimeSpan.FromSeconds(options.VisibilityTimeoutSeconds);
        var token = Guid.NewGuid();

        // Three stages, and each exists for a reason:
        //
        //   partitions — the partitions with work waiting, longest-waiting first, capped at the
        //                batch size. Ordering by the oldest waiting job is what makes this fair
        //                over time: a campaign that has been waiting rises to the front.
        //   ready      — the top few jobs *within each* of those partitions, via a lateral join.
        //                Taking a slice per partition rather than a global slice is the whole
        //                point: an earlier global-window version looked fair but wasn't, because
        //                a large campaign enqueued first simply filled the window and starved
        //                everything behind it. Also picks up rows whose lease has expired, which
        //                is how work orphaned by a killed instance is recovered with no separate
        //                reaper process.
        //   picked     — takes the actual row locks. SKIP LOCKED steps over rows a sibling
        //                instance is mid-claim on instead of blocking behind it, which is what
        //                lets many workers claim concurrently.
        //
        // The UPDATE then leases them in the same statement, so there is no window in which a row
        // has been selected but is not yet owned.
        const string sql = """
            WITH partitions AS (
                SELECT grouped.pk
                  FROM (
                      SELECT COALESCE("PartitionKey", '') AS pk, min("AvailableAt") AS oldest
                        FROM "JobQueue"
                       WHERE "QueueName" = @queue
                         AND "AvailableAt" <= now()
                         AND ("Status" = 'Pending'
                              OR ("Status" = 'Leased' AND "LeaseExpiresAt" IS NOT NULL AND "LeaseExpiresAt" < now()
                                  AND "Attempt" < "MaxAttempts"))
                       GROUP BY 1
                  ) grouped
                 ORDER BY grouped.oldest
                 LIMIT @batch_size
            ),
            ready AS (
                SELECT slice."Id"
                  FROM partitions p
                  CROSS JOIN LATERAL (
                      SELECT j2."Id"
                        FROM "JobQueue" j2
                       WHERE j2."QueueName" = @queue
                         AND COALESCE(j2."PartitionKey", '') = p.pk
                         AND j2."AvailableAt" <= now()
                         AND (j2."Status" = 'Pending'
                              OR (j2."Status" = 'Leased' AND j2."LeaseExpiresAt" IS NOT NULL AND j2."LeaseExpiresAt" < now()
                                  AND j2."Attempt" < j2."MaxAttempts"))
                       ORDER BY j2."Priority" DESC, j2."AvailableAt", j2."Id"
                       LIMIT @per_partition_cap
                  ) slice
            ),
            picked AS (
                SELECT j."Id"
                  FROM "JobQueue" j
                 WHERE j."Id" IN (SELECT "Id" FROM ready)
                   -- The claimability predicate is repeated here, at the same query level as
                   -- FOR UPDATE, and that repetition is load-bearing rather than redundant.
                   --
                   -- Under READ COMMITTED, every CTE in this statement shares one snapshot.
                   -- FOR UPDATE re-checks the row after taking its lock, but it re-checks only
                   -- *this* level's qualifiers. With the status test living solely in `ready`
                   -- above, a row that a sibling transaction leased and committed in the window
                   -- between the snapshot and the lock was never re-validated — so it could be
                   -- leased a second time, handing one job to two workers.
                   --
                   -- Restating it here means the post-lock re-check sees the current row version
                   -- and filters out anything that stopped being claimable in the meantime.
                   AND j."AvailableAt" <= now()
                   AND (j."Status" = 'Pending'
                        OR (j."Status" = 'Leased' AND j."LeaseExpiresAt" IS NOT NULL AND j."LeaseExpiresAt" < now()
                            -- A job whose final attempt lost its lease (the process died, or it
                            -- hung) is a poison candidate; the maintenance sweep dead-letters it
                            -- instead of letting it be claimed forever.
                            AND j."Attempt" < j."MaxAttempts"))
                 ORDER BY j."Priority" DESC, j."AvailableAt", j."Id"
                 FOR UPDATE SKIP LOCKED
                 LIMIT @batch_size
            )
            UPDATE "JobQueue" j
               SET "Status"         = 'Leased',
                   "LeaseToken"     = @token,
                   "LeasedBy"       = @worker,
                   "LeaseExpiresAt" = now() + make_interval(secs => @visibility_seconds),
                   "Attempt"        = j."Attempt" + 1,
                   "UpdatedAt"      = now()
              FROM picked p
             WHERE j."Id" = p."Id"
            RETURNING j."Id", j."LeaseToken", j."QueueName", j."Payload", j."Attempt",
                      j."MaxAttempts", j."PartitionKey", j."IdempotencyKey", j."LeaseExpiresAt"
            """;

        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("queue", request.QueueName);
        command.Parameters.AddWithValue("per_partition_cap", perPartitionCap);
        command.Parameters.AddWithValue("batch_size", batchSize);
        command.Parameters.AddWithValue("token", token);
        command.Parameters.AddWithValue("worker", request.WorkerId);
        command.Parameters.AddWithValue("visibility_seconds", visibility.TotalSeconds);

        var leases = new List<QueueLease>(batchSize);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            leases.Add(new QueueLease
            {
                JobId = reader.GetInt64(0),
                ReceiptToken = reader.GetGuid(1),
                QueueName = reader.GetString(2),
                Payload = reader.GetString(3),
                Attempt = reader.GetInt32(4),
                MaxAttempts = reader.GetInt32(5),
                PartitionKey = await reader.IsDBNullAsync(6, ct) ? null : reader.GetString(6),
                IdempotencyKey = await reader.IsDBNullAsync(7, ct) ? null : reader.GetString(7),
                LeaseExpiresAt = reader.GetDateTime(8)
            });
        }

        if (leases.Count > 0)
        {
            _logger.LogDebug(
                "Claimed {Count} job(s) from {Queue} as {Worker}.",
                leases.Count, request.QueueName, request.WorkerId);
        }

        return leases;
    }

    public async Task<bool> CompleteAsync(QueueLease lease, CancellationToken ct = default)
    {
        // Conditioned on the receipt token: a worker whose lease expired mid-job must not be able
        // to mark a job complete that another worker has since re-claimed and may still be
        // running.
        const string sql = """
            UPDATE "JobQueue"
               SET "Status"         = 'Completed',
                   "CompletedAt"    = now(),
                   "LeaseToken"     = NULL,
                   "LeaseExpiresAt" = NULL,
                   "UpdatedAt"      = now()
             WHERE "Id" = @id AND "LeaseToken" = @token
            """;

        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", lease.JobId);
        command.Parameters.AddWithValue("token", lease.ReceiptToken);

        var affected = await command.ExecuteNonQueryAsync(ct);
        if (affected == 0)
        {
            // Serious: the work was done, but we no longer owned the right to say so. Another
            // worker has probably repeated it. Surfaced loudly because the fix is operational —
            // the visibility timeout is too short for how long this handler takes.
            _logger.LogWarning(
                "Job {JobId} on {Queue} completed after its lease expired; another worker may have repeated it. "
              + "Consider raising Email:Queue:VisibilityTimeoutSeconds.",
                lease.JobId, lease.QueueName);
        }

        return affected > 0;
    }

    public async Task<bool> FailAsync(QueueLease lease, QueueFailure failure, CancellationToken ct = default)
    {
        // Two ways to be terminal: the provider said this can never work, or we have run out of
        // attempts. Retrying a permanent rejection four more times only delays the failure while
        // burning send quota and reputation.
        var deadLetter = !failure.IsTransient || lease.Attempt >= lease.MaxAttempts;
        var backoff = deadLetter ? TimeSpan.Zero : failure.RetryAfter ?? ComputeBackoff(lease.Attempt);

        const string sql = """
            UPDATE "JobQueue"
               SET "Status"         = CASE WHEN @dead_letter THEN 'DeadLettered' ELSE 'Pending' END,
                   "DeadLetteredAt" = CASE WHEN @dead_letter THEN now() ELSE NULL END,
                   "AvailableAt"    = CASE WHEN @dead_letter
                                           THEN "AvailableAt"
                                           ELSE now() + make_interval(secs => @backoff_seconds) END,
                   "LastError"      = @error,
                   "LastErrorAt"    = now(),
                   "LeaseToken"     = NULL,
                   "LeasedBy"       = NULL,
                   "LeaseExpiresAt" = NULL,
                   "UpdatedAt"      = now()
             WHERE "Id" = @id AND "LeaseToken" = @token
            """;

        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", lease.JobId);
        command.Parameters.AddWithValue("token", lease.ReceiptToken);
        command.Parameters.AddWithValue("dead_letter", deadLetter);
        command.Parameters.AddWithValue("backoff_seconds", backoff.TotalSeconds);
        command.Parameters.AddWithValue("error", Truncate(failure.Error, 4000));

        var affected = await command.ExecuteNonQueryAsync(ct);

        if (affected > 0 && deadLetter)
        {
            _logger.LogError(
                "Job {JobId} on {Queue} dead-lettered after {Attempt} attempt(s): {Error}",
                lease.JobId, lease.QueueName, lease.Attempt, failure.Error);
        }
        else if (affected > 0)
        {
            _logger.LogWarning(
                "Job {JobId} on {Queue} failed on attempt {Attempt}/{MaxAttempts}, retrying in {Backoff}s: {Error}",
                lease.JobId, lease.QueueName, lease.Attempt, lease.MaxAttempts,
                Math.Round(backoff.TotalSeconds, 1), failure.Error);
        }

        return affected > 0;
    }

    public async Task<bool> DeferAsync(QueueLease lease, TimeSpan delay, string reason, CancellationToken ct = default)
    {
        // Returns the job without spending an attempt. Used when the job could not even start
        // (no send capacity yet): counting that as a failure dead-lettered healthy jobs on any
        // campaign large enough to outrun its rate limit.
        const string sql = """
            UPDATE "JobQueue"
               SET "Status"         = 'Pending',
                   "Attempt"        = GREATEST("Attempt" - 1, 0),
                   "AvailableAt"    = now() + make_interval(secs => @delay_seconds),
                   "LastError"      = @reason,
                   "LeaseToken"     = NULL,
                   "LeasedBy"       = NULL,
                   "LeaseExpiresAt" = NULL,
                   "UpdatedAt"      = now()
             WHERE "Id" = @id AND "LeaseToken" = @token
            """;

        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", lease.JobId);
        command.Parameters.AddWithValue("token", lease.ReceiptToken);
        command.Parameters.AddWithValue("delay_seconds", Math.Max(0, delay.TotalSeconds));
        command.Parameters.AddWithValue("reason", Truncate(reason, 4000));

        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<int> DeadLetterExhaustedLeasesAsync(CancellationToken ct = default)
    {
        const string sql = """
            UPDATE "JobQueue"
               SET "Status"         = 'DeadLettered',
                   "DeadLetteredAt" = now(),
                   "LastError"      = 'Lease expired on the final attempt: the worker died or hung while processing this job.',
                   "LastErrorAt"    = now(),
                   "LeaseToken"     = NULL,
                   "LeasedBy"       = NULL,
                   "LeaseExpiresAt" = NULL,
                   "UpdatedAt"      = now()
             WHERE "Status" = 'Leased'
               AND "LeaseExpiresAt" < now()
               AND "Attempt" >= "MaxAttempts"
            """;

        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        var affected = await command.ExecuteNonQueryAsync(ct);

        if (affected > 0)
        {
            _logger.LogError("Dead-lettered {Count} job(s) whose final attempt lost its lease.", affected);
        }

        return affected;
    }

    public async Task<bool> ExtendLeaseAsync(QueueLease lease, TimeSpan extension, CancellationToken ct = default)
    {
        // Also conditioned on the lease not having already lapsed: if it has, the honest answer
        // is false so the handler abandons rather than finishing work it no longer owns.
        const string sql = """
            UPDATE "JobQueue"
               SET "LeaseExpiresAt" = now() + make_interval(secs => @extension_seconds),
                   "UpdatedAt"      = now()
             WHERE "Id" = @id
               AND "LeaseToken" = @token
               AND "Status" = 'Leased'
               AND "LeaseExpiresAt" > now()
            """;

        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", lease.JobId);
        command.Parameters.AddWithValue("token", lease.ReceiptToken);
        command.Parameters.AddWithValue("extension_seconds", extension.TotalSeconds);

        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<QueueDepthSnapshot> GetDepthAsync(string queueName, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                count(*) FILTER (WHERE "Status" = 'Pending')                                        AS pending,
                count(*) FILTER (WHERE "Status" = 'Leased')                                         AS leased,
                count(*) FILTER (WHERE "Status" = 'DeadLettered')                                   AS dead_lettered,
                count(*) FILTER (WHERE "Status" = 'Completed')                                      AS completed,
                count(*) FILTER (WHERE "Status" = 'Leased' AND "LeaseExpiresAt" < now())            AS expired_leases,
                min("CreatedAt") FILTER (WHERE "Status" = 'Pending' AND "AvailableAt" <= now())     AS oldest_pending
              FROM "JobQueue"
             WHERE "QueueName" = @queue
            """;

        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("queue", queueName);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return new QueueDepthSnapshot { QueueName = queueName, Transport = TransportName };
        }

        return new QueueDepthSnapshot
        {
            QueueName = queueName,
            Transport = TransportName,
            Pending = (int)reader.GetInt64(0),
            Leased = (int)reader.GetInt64(1),
            DeadLettered = (int)reader.GetInt64(2),
            Completed = (int)reader.GetInt64(3),
            ExpiredLeases = (int)reader.GetInt64(4),
            OldestPendingAt = await reader.IsDBNullAsync(5, ct) ? null : reader.GetDateTime(5)
        };
    }

    public async Task<int> RequeueDeadLetteredAsync(string queueName, int maxCount, CancellationToken ct = default)
    {
        // Attempt is reset so the requeued job gets a full set of retries again. Anything else
        // would have it dead-letter on its first stumble, which defeats the point of requeueing
        // once the underlying problem is fixed.
        const string sql = """
            WITH picked AS (
                SELECT "Id" FROM "JobQueue"
                 WHERE "QueueName" = @queue AND "Status" = 'DeadLettered'
                 ORDER BY "DeadLetteredAt"
                 FOR UPDATE SKIP LOCKED
                 LIMIT @max_count
            )
            UPDATE "JobQueue" j
               SET "Status"         = 'Pending',
                   "Attempt"        = 0,
                   "AvailableAt"    = now(),
                   "DeadLetteredAt" = NULL,
                   "LeaseToken"     = NULL,
                   "LeasedBy"       = NULL,
                   "LeaseExpiresAt" = NULL,
                   "UpdatedAt"      = now()
              FROM picked p
             WHERE j."Id" = p."Id"
            """;

        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("queue", queueName);
        command.Parameters.AddWithValue("max_count", maxCount);

        var affected = await command.ExecuteNonQueryAsync(ct);
        if (affected > 0)
        {
            _logger.LogInformation("Requeued {Count} dead-lettered job(s) on {Queue}.", affected, queueName);
        }

        return affected;
    }

    /// <summary>
    /// Exponential backoff with jitter, capped.
    ///
    /// <para>
    /// The jitter is not decoration. Without it, a provider outage fails every job in a batch at
    /// the same instant, they all retry at the same instant, and the thundering herd arrives the
    /// moment the provider recovers — which is often enough to knock it over again.
    /// </para>
    /// </summary>
    private TimeSpan ComputeBackoff(int attempt)
    {
        var options = Queue;

        // Shift rather than Math.Pow, and clamped before it can overflow: attempt is bounded by
        // MaxAttempts in practice, but a requeued job with a hand-edited attempt count should not
        // produce an infinite delay.
        var exponent = Math.Min(Math.Max(attempt - 1, 0), 20);
        var seconds = options.BaseBackoffSeconds * Math.Pow(2, exponent);
        seconds = Math.Min(seconds, options.MaxBackoffSeconds);

        // Symmetric jitter around the computed delay, floored so a retry is never immediate.
        var jitter = seconds * options.JitterFactor;
        var jittered = seconds - jitter + Random.Shared.NextDouble() * jitter * 2;

        return TimeSpan.FromSeconds(Math.Max(1, jittered));
    }

    private static string Truncate(string value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}
