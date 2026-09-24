using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// An append-only log of everything SES told us about a sent message.
///
/// <para>
/// Append-only on purpose. Delivery state on the recipient row is a lossy summary, guarded to
/// move only forwards, so it cannot answer "did this bounce soft or hard, and what did the remote
/// server actually say". Opens and clicks live here exclusively — they are engagement, not
/// delivery, and folding them into MessageStatus would let a later open regress a recipient out
/// of Delivered.
/// </para>
/// </summary>
public class EmailDeliveryEvent
{
    public long Id { get; set; }

    public int? CampaignContactId { get; set; }
    public CampaignContact? CampaignContact { get; set; }

    public int? ChatMessageId { get; set; }
    public ChatMessage? ChatMessage { get; set; }

    /// <summary>
    /// The SES MessageId this event is about. Indexed — it is the only correlation key SES gives
    /// us, and events routinely arrive before the send row has finished being written.
    /// </summary>
    [Required, MaxLength(255)]
    public string ProviderMessageId { get; set; } = string.Empty;

    public EmailEventType EventType { get; set; }

    public DateTime OccurredAt { get; set; }

    /// <summary>
    /// SNS's own message id, uniquely indexed. SNS guarantees at-least-once delivery and will
    /// happily redeliver the same notification, so this is what makes the handler idempotent.
    /// </summary>
    [Required, MaxLength(255)]
    public string SnsMessageId { get; set; } = string.Empty;

    public EmailBounceType? BounceType { get; set; }

    [MaxLength(100)]
    public string? BounceSubType { get; set; }

    /// <summary>
    /// The remote SMTP server's verbatim rejection, which is usually the only thing that actually
    /// explains a bounce to an operator.
    /// </summary>
    [MaxLength(2000)]
    public string? DiagnosticCode { get; set; }

    [MaxLength(255)]
    public string? RecipientAddress { get; set; }

    /// <summary>Set for Click events.</summary>
    [MaxLength(2000)]
    public string? LinkUrl { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    /// <summary>The raw notification, passed through PayloadRedactor first.</summary>
    public string? PayloadJson { get; set; }

    public DateTime CreatedAt { get; set; }
}
