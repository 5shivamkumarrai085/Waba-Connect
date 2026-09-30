namespace WhatsAppCampaignApi.Services.Queue;

/// <summary>
/// A durable, at-least-once work queue.
///
/// <para>
/// Nothing in this contract mentions storage, and that is its entire purpose. The campaign and
/// email workers are written against this interface alone, so replacing the PostgreSQL transport
/// with AWS SQS for a higher-scale deployment is a DI registration change rather than a rewrite:
/// <c>ClaimAsync</c> maps to ReceiveMessage, <c>CompleteAsync</c> to DeleteMessage,
/// <c>ExtendLeaseAsync</c> to ChangeMessageVisibility, and <c>FailAsync</c> to a visibility-timeout
/// backoff plus a redrive policy.
/// </para>
/// <para>
/// Delivery is at-least-once, never exactly-once — no queue can promise otherwise across a worker
/// that dies mid-job. Callers must therefore be idempotent. Two mechanisms are provided for that:
/// <see cref="QueueMessage.IdempotencyKey"/> makes duplicate <em>enqueues</em> a no-op, and
/// <see cref="QueueLease.Attempt"/> lets a handler recognise a retry. Guarding the side effect
/// itself remains the handler's job.
/// </para>
/// </summary>
public interface IJobQueue
{
    /// <summary>Which transport is in use. Diagnostics and the monitoring endpoint only.</summary>
    string TransportName { get; }

    /// <summary>
    /// Adds messages to their queues. Messages carrying an <see cref="QueueMessage.IdempotencyKey"/>
    /// already present on that queue are silently skipped and reported as duplicates, which is
    /// what makes re-expanding a campaign after a crash safe.
    /// </summary>
    Task<QueueEnqueueResult> EnqueueAsync(IReadOnlyCollection<QueueMessage> messages, CancellationToken ct = default);

    /// <summary>
    /// Takes exclusive leases on up to <see cref="QueueClaimRequest.BatchSize"/> ready jobs.
    /// Safe to call concurrently from any number of instances: no job is ever handed to two
    /// callers while its lease is live. Returns an empty list when nothing is ready — callers are
    /// expected to back off rather than spin.
    /// </summary>
    Task<IReadOnlyList<QueueLease>> ClaimAsync(QueueClaimRequest request, CancellationToken ct = default);

    /// <summary>
    /// Marks a leased job done. Returns false when the lease was no longer ours — meaning it
    /// expired and another worker may have picked the job up, so the side effect may have
    /// happened twice. Callers should log that rather than ignore it.
    /// </summary>
    Task<bool> CompleteAsync(QueueLease lease, CancellationToken ct = default);

    /// <summary>
    /// Reports a failed job. A transient failure with attempts remaining is rescheduled with
    /// exponential backoff and jitter; anything else is dead-lettered. Returns false when the
    /// lease was already lost.
    /// </summary>
    Task<bool> FailAsync(QueueLease lease, QueueFailure failure, CancellationToken ct = default);

    /// <summary>
    /// Pushes the lease's expiry further out, for a handler still working. Returns false if the
    /// lease has already expired, which tells the handler to abandon rather than finish — another
    /// worker now owns the job.
    /// </summary>
    Task<bool> ExtendLeaseAsync(QueueLease lease, TimeSpan extension, CancellationToken ct = default);

    /// <summary>
    /// Returns a leased job to the queue after <paramref name="delay"/> WITHOUT consuming an
    /// attempt. For work that could not start — no rate-limit capacity yet — as opposed to work
    /// that started and failed, which is <see cref="FailAsync"/>.
    /// </summary>
    Task<bool> DeferAsync(QueueLease lease, TimeSpan delay, string reason, CancellationToken ct = default);

    /// <summary>
    /// Dead-letters jobs whose final attempt lost its lease — the process died or hung mid-job.
    /// Without this such a job is re-claimable forever and can crash every worker that takes it.
    /// </summary>
    Task<int> DeadLetterExhaustedLeasesAsync(CancellationToken ct = default);

    /// <summary>Queue depth and age, for monitoring and alerting.</summary>
    Task<QueueDepthSnapshot> GetDepthAsync(string queueName, CancellationToken ct = default);

    /// <summary>
    /// Returns dead-lettered jobs to the queue. An explicit operator action: whatever broke them
    /// usually needs fixing first, so this is never automatic.
    /// </summary>
    Task<int> RequeueDeadLetteredAsync(string queueName, int maxCount, CancellationToken ct = default);
}

/// <summary>A job to be enqueued.</summary>
public sealed record QueueMessage
{
    public required string QueueName { get; init; }

    /// <summary>The job's arguments, serialised as JSON.</summary>
    public required string Payload { get; init; }

    /// <summary>
    /// De-duplication key, unique per queue. Enqueueing the same key twice adds one job. Null
    /// means no de-duplication, and every enqueue creates a new job.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>
    /// Fairness group — the campaign id, for email sends. The claim path caps how many jobs one
    /// partition may contribute to a single batch, so a very large campaign cannot monopolise
    /// the workers.
    /// </summary>
    public string? PartitionKey { get; init; }

    /// <summary>Earliest time the job may run. Null means immediately.</summary>
    public DateTime? AvailableAt { get; init; }

    /// <summary>Overrides the configured default.</summary>
    public int? MaxAttempts { get; init; }

    /// <summary>Higher runs first. Transactional mail should outrank a bulk campaign.</summary>
    public int Priority { get; init; }
}

/// <param name="Enqueued">Jobs actually created.</param>
/// <param name="Duplicates">Jobs skipped because their idempotency key already existed.</param>
public sealed record QueueEnqueueResult(int Enqueued, int Duplicates);

public sealed record QueueClaimRequest
{
    public required string QueueName { get; init; }

    /// <summary>Identifies the claiming instance, for diagnostics.</summary>
    public required string WorkerId { get; init; }

    public int BatchSize { get; init; } = 10;

    /// <summary>Overrides the configured visibility timeout.</summary>
    public TimeSpan? VisibilityTimeout { get; init; }

    /// <summary>Overrides the configured per-partition fairness cap.</summary>
    public int? PerPartitionCap { get; init; }
}

/// <summary>An exclusive, time-limited claim on one job.</summary>
public sealed record QueueLease
{
    public required long JobId { get; init; }

    /// <summary>
    /// Proves ownership. Every state transition is conditioned on it, so a worker whose lease
    /// expired cannot later overwrite the state of a job another worker now owns.
    /// </summary>
    public required Guid ReceiptToken { get; init; }

    public required string QueueName { get; init; }
    public required string Payload { get; init; }

    /// <summary>1 on first delivery. Greater than 1 means this job has been tried before, which
    /// is the handler's cue to check whether its side effect already happened.</summary>
    public required int Attempt { get; init; }

    public required int MaxAttempts { get; init; }
    public string? PartitionKey { get; init; }
    public string? IdempotencyKey { get; init; }
    public required DateTime LeaseExpiresAt { get; init; }

    /// <summary>True when a further transient failure would dead-letter the job. Lets a handler
    /// escalate its logging, or record a terminal error on its own domain row.</summary>
    public bool IsFinalAttempt => Attempt >= MaxAttempts;
}

/// <param name="Error">Operator-facing description. Stored, so it must not contain secrets.</param>
/// <param name="IsTransient">
/// Whether retrying could plausibly succeed. Throttling, timeouts and 5xx are transient; a
/// malformed address or a rejected sender is not, and retrying it four more times only delays the
/// failure while consuming send quota.
/// </param>
/// <param name="RetryAfter">
/// Honour a provider-specified delay instead of the computed backoff — a mail server may ask for one
/// when throttling.
/// </param>
public sealed record QueueFailure(string Error, bool IsTransient, TimeSpan? RetryAfter = null);

public sealed record QueueDepthSnapshot
{
    public required string QueueName { get; init; }
    public required string Transport { get; init; }
    public int Pending { get; init; }
    public int Leased { get; init; }
    public int DeadLettered { get; init; }
    public int Completed { get; init; }

    /// <summary>
    /// When the oldest ready job was queued. The number that actually matters: depth alone
    /// cannot distinguish a healthy burst from a stalled queue.
    /// </summary>
    public DateTime? OldestPendingAt { get; init; }

    /// <summary>Jobs whose lease has lapsed and which are awaiting reclaim. Persistently non-zero
    /// means workers are dying mid-job, or the visibility timeout is too short.</summary>
    public int ExpiredLeases { get; init; }
}
