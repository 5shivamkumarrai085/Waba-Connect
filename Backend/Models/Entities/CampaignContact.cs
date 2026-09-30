using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Models.Entities;

public class CampaignContact
{
    public int Id { get; set; }
    
    public int CampaignId { get; set; }
    public Campaign Campaign { get; set; } = null!;
    
    public int ContactId { get; set; }
    public Contact Contact { get; set; } = null!;
    
    [MaxLength(200)]
    public string? WhatsAppMessageId { get; set; }

    /// <summary>
    /// The sending provider's own id for this recipient's message — the Message-Id header for email.
    /// Deliberately a second column rather than a rename of <see cref="WhatsAppMessageId"/>:
    /// renaming would touch the webhook correlation path, the reporting queries and the chat
    /// lookups, all of which are WhatsApp-critical, for no gain. WhatsApp keeps writing the old
    /// column; email writes only this one.
    /// </summary>
    [MaxLength(255)]
    public string? ProviderMessageId { get; set; }

    public MessageStatus Status { get; set; } = MessageStatus.Pending;

    /// <summary>
    /// Stamped immediately before the provider call, and before the send result is committed.
    /// SMTP offers no idempotent send, so if a worker dies mid-send this is the only evidence
    /// that a message may already be in flight: a redelivered job that finds this set but no
    /// ProviderMessageId is treated as "possibly sent" rather than silently sent again.
    /// </summary>
    public DateTime? SendAttemptedAt { get; set; }
    
    [MaxLength(500)]
    public string? ErrorMessage { get; set; }
    
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }

    // ── Open / click tracking ─────────────────────────────────────────────────────────────
    /// <summary>
    /// A random, opaque token that identifies this recipient in tracking URLs.
    /// Generated at expansion time. Never exposes CampaignContactId or ContactId in the URL,
    /// so scanning tracking links reveals nothing about the underlying data model.
    /// </summary>
    [MaxLength(64)]
    public string? TrackingId { get; set; }

    // Engagement timestamps. Set on first occurrence; not overwritten by later events.
    // These are engagement data, not delivery state — they never change CampaignContact.Status.
    public DateTime? OpenedAt { get; set; }
    public DateTime? ClickedAt { get; set; }
    public DateTime? RepliedAt { get; set; }

    /// <summary>A/B tests: the variant this recipient receives (null outside a test).</summary>
    public int? VariantId { get; set; }

    /// <summary>A/B tests: waiting for the winner; not sent until the test is decided.</summary>
    public bool HeldForWinner { get; set; }
}
