using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Options;

/// <summary>
/// Configuration for the email channel, bound from the <c>Email</c> section.
///
/// <para>
/// This is a deliberate departure from the rest of the codebase, which reads
/// <c>IConfiguration</c> inline at every use site. That is workable for the handful of knobs
/// WhatsApp needs; it is not workable here, where a wrong visibility timeout or backoff base
/// produces duplicate sends or a stalled queue rather than an obvious error. Binding once with
/// <c>ValidateOnStart</c> turns those into a startup failure instead of a production incident.
/// </para>
/// <para>
/// No secret belongs in this section. Provider credentials live per-connection, AES-encrypted, in
/// EmailConfiguration; the unsubscribe signing key comes from the environment. Anything here is
/// safe to commit.
/// </para>
/// </summary>
public class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// Master switch. When false the workers do not start and the email endpoints report the
    /// channel as unavailable — which is what keeps a half-configured deployment from silently
    /// accepting campaigns it can never send.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Provider used when a connection does not name one. "AmazonSes" or "Smtp".</summary>
    [Required]
    public string DefaultProvider { get; set; } = "AmazonSes";

    [Required]
    public QueueOptions Queue { get; set; } = new();

    [Required]
    public DispatchOptions Dispatch { get; set; } = new();

    [Required]
    public ExpansionOptions Expansion { get; set; } = new();

    [Required]
    public UnsubscribeOptions Unsubscribe { get; set; } = new();

    [Required]
    public SesOptions Ses { get; set; } = new();

    [Required]
    public InboundOptions Inbound { get; set; } = new();

    [Required]
    public ExecutionGateOptions ExecutionGate { get; set; } = new();
}

/// <summary>Queue transport and retry behaviour.</summary>
public class QueueOptions
{
    /// <summary>
    /// "Postgres" today. The workers never see this — it selects which IJobQueue implementation
    /// is registered, which is the whole point of the abstraction.
    /// </summary>
    [Required]
    public string Transport { get; set; } = "Postgres";

    /// <summary>How many jobs one claim round trip may take.</summary>
    [Range(1, 1000)]
    public int ClaimBatchSize { get; set; } = 25;

    /// <summary>
    /// Visibility timeout. Must comfortably exceed the worst-case time to process one job, or a
    /// slow provider call will let a second worker claim work that is still in flight. The
    /// dispatch worker also heartbeats the lease, so this is the ceiling on how long an orphaned
    /// job stays invisible, not a per-job deadline.
    /// </summary>
    [Range(10, 3600)]
    public int VisibilityTimeoutSeconds { get; set; } = 120;

    [Range(1, 50)]
    public int MaxAttempts { get; set; } = 5;

    [Range(1, 3600)]
    public int BaseBackoffSeconds { get; set; } = 10;

    [Range(1, 86400)]
    public int MaxBackoffSeconds { get; set; } = 900;

    /// <summary>
    /// Randomisation applied to each backoff, as a fraction. Without jitter, a provider outage
    /// makes every failed job in a batch retry in lockstep and hammer the provider the moment it
    /// recovers.
    /// </summary>
    [Range(0, 1)]
    public double JitterFactor { get; set; } = 0.2;

    [Range(50, 60000)]
    public int PollIntervalMs { get; set; } = 1000;

    /// <summary>Backoff applied when a claim comes back empty, so an idle queue is not polled at
    /// full rate.</summary>
    [Range(100, 300000)]
    public int IdleBackoffMs { get; set; } = 5000;

    /// <summary>
    /// Fairness cap: the most jobs one partition (one campaign) may contribute to a single claim
    /// batch. This is what stops a 100k-recipient campaign from starving everything queued
    /// behind it. Set it to ClaimBatchSize to disable fairness.
    /// </summary>
    [Range(1, 1000)]
    public int PerPartitionCap { get; set; } = 5;

    /// <summary>How long completed rows are kept before the maintenance sweep prunes them.</summary>
    [Range(0, 365)]
    public int CompletedRetentionDays { get; set; } = 7;
}

/// <summary>Email dispatch worker behaviour.</summary>
public class DispatchOptions
{
    /// <summary>Concurrent dispatch loops per instance. The real send rate is bounded by the
    /// per-connection token bucket, not by this.</summary>
    [Range(1, 64)]
    public int WorkerCount { get; set; } = 2;

    [Range(1, 500)]
    public int BatchSize { get; set; } = 10;

    /// <summary>
    /// Fallback send rate for a connection that specifies none. Deliberately conservative: SES
    /// starts new accounts at 1/sec, and guessing high is how an account gets throttled and then
    /// reputation-damaged.
    /// </summary>
    [Range(0.1, 1000)]
    public double DefaultSendRatePerSecond { get; set; } = 1;

    /// <summary>Per-send timeout for the provider call.</summary>
    [Range(1, 600)]
    public int SendTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// How long to wait for an SMTP server to answer before giving up, in seconds.
    ///
    /// <para>
    /// MailKit's own default is 120 seconds. That is far too long for a form the operator is
    /// waiting on — and worse, it outlives the frontend's HTTP timeout, so a misconfigured
    /// connection surfaced as a generic client-side timeout instead of the server's explanation
    /// of what was wrong. Twenty seconds is ample for any reachable relay.
    /// </para>
    /// </summary>
    [Range(5, 120)]
    public int SmtpTimeoutSeconds { get; set; } = 20;
}

/// <summary>Campaign expansion worker behaviour.</summary>
public class ExpansionOptions
{
    /// <summary>Recipients read per page while expanding. Bounds memory for a large campaign.</summary>
    [Range(10, 10000)]
    public int RecipientPageSize { get; set; } = 500;

    /// <summary>Jobs enqueued per insert round trip.</summary>
    [Range(1, 1000)]
    public int JobBatchSize { get; set; } = 100;
}

/// <summary>One-click unsubscribe (RFC 8058) settings.</summary>
public class UnsubscribeOptions
{
    /// <summary>
    /// Public base URL the unsubscribe link is built from. Must be publicly reachable — a
    /// localhost value produces links that silently fail in every recipient's mail client.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    /// HMAC key for unsubscribe tokens. Supplied by environment variable
    /// (<c>Email__Unsubscribe__SigningKey</c>) and never committed. Validated at startup only
    /// when the channel is enabled, so a dev machine without it still boots.
    /// </summary>
    public string? SigningKey { get; set; }

    /// <summary>
    /// Token lifetime. Long, because recipients act on old mail — an expired link that forces
    /// someone to hunt for a support address is a compliance problem, not a security win.
    /// </summary>
    [Range(1, 3650)]
    public int TokenTtlDays { get; set; } = 400;
}

/// <summary>Amazon SES specifics that are account-wide rather than per-connection.</summary>
public class SesOptions
{
    /// <summary>
    /// Default configuration set for event publishing, when a connection names none. Without one
    /// SES still sends, but no delivery, bounce or complaint event ever arrives.
    /// </summary>
    public string? EventConfigurationSet { get; set; }

    /// <summary>
    /// SNS topic ARNs the webhook will accept. An allowlist, not decoration: the endpoint is
    /// necessarily anonymous, and signature verification alone would accept a validly-signed
    /// notification from any SNS topic in any AWS account.
    /// </summary>
    public string[] AllowedSnsTopicArns { get; set; } = [];

    /// <summary>S3 bucket the SES receipt rule writes raw inbound MIME to.</summary>
    public string? InboundBucket { get; set; }

    public string? InboundPrefix { get; set; }

    /// <summary>
    /// The value recipients must include in their SPF record, e.g. "amazonses.com". Data, not a
    /// literal in code, because it differs by provider and region.
    /// </summary>
    public string SpfInclude { get; set; } = "amazonses.com";

    /// <summary>
    /// Template for the DMARC record shown in the setup UI. Starts at p=none so a new domain
    /// does not begin rejecting its own mail before the operator has read the reports.
    /// </summary>
    public string DmarcPolicyTemplate { get; set; } = "v=DMARC1; p=none; rua=mailto:{rua}";
}

/// <summary>Inbound reply handling.</summary>
public class InboundOptions
{
    public bool Enabled { get; set; }

    /// <summary>
    /// How often the IMAP polling worker checks each mailbox for new replies, in seconds.
    /// Faster means more responsive inbound but more IMAP connections.
    /// </summary>
    [Range(10, 3600)]
    public int PollIntervalSeconds { get; set; } = 60;

    [Range(1024, 52428800)]
    public int MaxSizeBytes { get; set; } = 10485760;

    /// <summary>
    /// Headers that mark a message as machine-generated. Anything carrying one is recorded but
    /// never auto-replied to — this is what stops two auto-responders from mailing each other
    /// indefinitely.
    /// </summary>
    public string[] LoopProtectionHeaders { get; set; } = ["Auto-Submitted", "X-Auto-Response-Suppress", "Precedence"];
}

/// <summary>
/// Selects who authorises campaign execution.
/// </summary>
public class ExecutionGateOptions
{
    /// <summary>
    /// "none" means auto-approve, which is this deployment's behaviour. A host application that
    /// owns maker-checker registers its own ICampaignExecutionGate and sets this to name it; no
    /// worker, queue or provider code changes when it does.
    /// </summary>
    [Required]
    public string Provider { get; set; } = "none";
}
