using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// Normalized, channel-agnostic email event history.
///
/// <para>
/// Every source that can tell us something happened to an email — SMTP result, IMAP polling,
/// open-tracking pixel, click-tracking redirect, unsubscribe link — writes one row here, in the
/// same shape, instead of routing to different tables. This is the single source of truth for
/// event history and the foundation for any future reporting queries.
/// </para>
///
/// <para>
/// This is append-only by design. CampaignContact holds the latest/current state (what the
/// recipient's status is right now). This table holds the complete history (what has happened,
/// and when). They serve different queries and must not be merged.
/// </para>
///
/// <para>
/// Idempotency is enforced by the unique index on <see cref="IdempotencyKey"/>. The event
/// processor checks this before writing, but the index is the safety net that keeps a
/// re-delivered webhook or a restarted worker from creating duplicate counter increments.
/// </para>
/// </summary>
public class EmailEvent
{
    public long Id { get; set; }

    // ── Campaign context ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Which campaign this event belongs to. Indexed for campaign-level reporting.
    /// Nullable: one-off (non-campaign) emails may generate Open/Click events too.
    /// </summary>
    public int? CampaignId { get; set; }
    public Campaign? Campaign { get; set; }

    /// <summary>
    /// The specific recipient within the campaign. Indexed for recipient-level reporting.
    /// </summary>
    public int? CampaignContactId { get; set; }
    public CampaignContact? CampaignContact { get; set; }

    // ── Message correlation ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// The Message-ID header of the email this event concerns (RFC 5322 format, angle-bracketed).
    /// Used to correlate open/click events with the message that was sent.
    /// </summary>
    [MaxLength(500)]
    public string? MessageId { get; set; }

    /// <summary>
    /// The provider's own identifier for the message (SMTP ENVID, future SES MessageId, etc.).
    /// Nullable: not all sources provide one.
    /// </summary>
    [MaxLength(255)]
    public string? ProviderMessageId { get; set; }

    // ── Event core ────────────────────────────────────────────────────────────────────────────

    public EmailEventKind EventKind { get; set; }

    /// <summary>
    /// When this event actually occurred, according to the source that reported it.
    /// Not the time we received or stored the event — they are often different.
    /// </summary>
    public DateTime OccurredAt { get; set; }

    // ── Idempotency ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A stable, source-scoped unique key for this event. Uniquely indexed.
    ///
    /// <para>
    /// Convention: {source}:{source-specific-id}
    /// Examples:
    ///   smtp:send:{campaignContactId}
    ///   imap:reply:{messageId}:{receivedAt:yyyyMMddHHmmss}
    ///   pixel:{trackingId}  (unique-open: pixel:{trackingId}:unique)
    ///   click:{trackingId}:{linkIndex}:{timestamp:yyyyMMddHHmm}
    ///   unsub:{campaignId}:{contactId}
    ///   bounce:imap:{messageId}
    /// </para>
    /// </summary>
    [Required, MaxLength(400)]
    public string IdempotencyKey { get; set; } = string.Empty;

    // ── Source ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Which subsystem produced this event: "Smtp", "Imap", "Pixel", "Click", "Unsubscribe",
    /// "Dsn", "Webhook".
    /// </summary>
    [MaxLength(50)]
    public string Source { get; set; } = string.Empty;

    // ── Bounce detail ─────────────────────────────────────────────────────────────────────────

    public EmailBounceType? BounceType { get; set; }

    [MaxLength(100)]
    public string? BounceSubType { get; set; }

    [MaxLength(2000)]
    public string? DiagnosticCode { get; set; }

    // ── Click detail ──────────────────────────────────────────────────────────────────────────

    /// <summary>Set for CLICKED events. The original URL before wrapping.</summary>
    [MaxLength(2000)]
    public string? OriginalUrl { get; set; }

    // ── Open / click context ──────────────────────────────────────────────────────────────────

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    // ── Recipient address ─────────────────────────────────────────────────────────────────────

    [MaxLength(255)]
    public string? RecipientAddress { get; set; }

    // ── Metadata ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Additional structured data. Stored as JSON text.</summary>
    public string? MetadataJson { get; set; }

    public DateTime CreatedAt { get; set; }
}
