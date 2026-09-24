using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// One unit of background work, in a PostgreSQL-backed queue.
///
/// <para>
/// This entity exists so EF can create and migrate the table, and so the monitoring endpoint can
/// read it with ordinary LINQ. The claim path deliberately does <em>not</em> go through EF: it is
/// a single hand-written <c>FOR UPDATE SKIP LOCKED</c> statement in PostgresJobQueue, because the
/// atomicity of "find work, mark it mine, extend the lease" cannot be expressed through the
/// change tracker without a race between instances.
/// </para>
/// <para>
/// Nothing outside PostgresJobQueue should reference this type. Workers talk to IJobQueue, whose
/// contract mentions no storage at all — that is what lets AWS SQS replace the transport later
/// without either worker changing.
/// </para>
/// </summary>
public class JobQueueEntry
{
    public long Id { get; set; }

    /// <summary>Logical queue, e.g. "campaign-expansion" or "email-send". Leading column of the
    /// claim index.</summary>
    [Required, MaxLength(100)]
    public string QueueName { get; set; } = string.Empty;

    /// <summary>
    /// Fairness key — the campaign id, for email sends. The claim query caps how many rows one
    /// partition may contribute to a single batch, which is what stops a 100k-recipient campaign
    /// from starving every other campaign behind it.
    /// </summary>
    [MaxLength(100)]
    public string? PartitionKey { get; set; }

    /// <summary>The job's arguments, as JSON. Stored as jsonb.</summary>
    [Required]
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Caller-supplied de-duplication key, uniquely indexed per queue. Enqueueing the same key
    /// twice is a no-op, which is what makes campaign expansion safe to re-run after a crash
    /// instead of double-sending.
    /// </summary>
    [MaxLength(200)]
    public string? IdempotencyKey { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Pending;

    /// <summary>Higher runs first. Transactional mail should outrank a bulk campaign.</summary>
    public int Priority { get; set; }

    public int Attempt { get; set; }

    public int MaxAttempts { get; set; }

    /// <summary>
    /// Earliest time this job may be claimed. Doubles as the scheduling mechanism and the
    /// retry-backoff mechanism — a failed job is simply pushed into the future.
    /// </summary>
    public DateTime AvailableAt { get; set; }

    /// <summary>Identifies the current lease holder, so only it can complete or fail the job.</summary>
    public Guid? LeaseToken { get; set; }

    /// <summary>Which worker instance holds the lease. Diagnostics only.</summary>
    [MaxLength(200)]
    public string? LeasedBy { get; set; }

    /// <summary>
    /// Visibility timeout. Once this passes, the claim query treats the row as available again —
    /// which is how work orphaned by a killed instance gets picked up, with no separate reaper.
    /// </summary>
    public DateTime? LeaseExpiresAt { get; set; }

    [MaxLength(4000)]
    public string? LastError { get; set; }

    public DateTime? LastErrorAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>Set when the job exhausted its attempts or failed non-transiently. Terminal until
    /// an operator explicitly requeues it.</summary>
    public DateTime? DeadLetteredAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
