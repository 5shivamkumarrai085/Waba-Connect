using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Models.Entities;

public class ChatMessage
{
    public int Id { get; set; }

    public int ConversationId { get; set; }
    public ChatConversation Conversation { get; set; } = null!;

    public int? ContactId { get; set; }
    public Contact? Contact { get; set; }

    public int? ConnectionId { get; set; }
    public virtual Connection? Connection { get; set; }

    public int? CampaignId { get; set; }
    public Campaign? Campaign { get; set; }

    public int? CampaignContactId { get; set; }
    public CampaignContact? CampaignContact { get; set; }

    [MaxLength(200)]
    public string? WhatsAppMessageId { get; set; }

    /// <summary>
    /// Which channel carried this message. Defaults to WhatsApp so existing rows and the
    /// existing chat queries (which do not filter on channel) are unaffected.
    /// </summary>
    public MessageChannel Channel { get; set; } = MessageChannel.WhatsApp;

    /// <summary>
    /// Channel-neutral provider id — an SES MessageId for email. See the note on
    /// CampaignContact.ProviderMessageId for why this sits beside WhatsAppMessageId rather
    /// than replacing it.
    /// </summary>
    [MaxLength(255)]
    public string? ProviderMessageId { get; set; }

    /// <summary>Email-specific envelope and body, present only for email messages.</summary>
    public EmailMessageDetail? EmailDetail { get; set; }

    public ChatMessageDirection Direction { get; set; }
    public ChatMessageStatus Status { get; set; } = ChatMessageStatus.Pending;

    [Required, MaxLength(4096)]
    public string Text { get; set; } = string.Empty;

    public bool IsTemplate { get; set; }

    [MaxLength(1000)]
    public string? MediaUrl { get; set; }

    [MaxLength(50)]
    public string? MediaType { get; set; }

    [MaxLength(255)]
    public string? MediaFileName { get; set; }

    [MaxLength(1000)]
    public string? ErrorMessage { get; set; }

    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
    /// <summary>
    /// Soft delete. Deleting a message here removes it from OmniConnect only — WhatsApp gives us
    /// no way to unsend from the recipient's phone — so the row is kept both to make the action
    /// recoverable and so the audit trail can still point at something real.
    /// </summary>
    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    /// <summary>Null when a message is removed by something other than a signed-in user.</summary>
    public int? DeletedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
