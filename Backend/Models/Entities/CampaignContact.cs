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
    /// The sending provider's own id for this recipient's message — an SES MessageId for email.
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
    /// SES offers no idempotent send, so if a worker dies mid-send this is the only evidence
    /// that a message may already be in flight: a redelivered job that finds this set but no
    /// ProviderMessageId is treated as "possibly sent" rather than silently sent again.
    /// </summary>
    public DateTime? SendAttemptedAt { get; set; }
    
    [MaxLength(500)]
    public string? ErrorMessage { get; set; }
    
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
