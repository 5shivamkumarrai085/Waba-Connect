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

    public int? CampaignId { get; set; }
    public Campaign? Campaign { get; set; }

    public int? CampaignContactId { get; set; }
    public CampaignContact? CampaignContact { get; set; }

    [MaxLength(200)]
    public string? WhatsAppMessageId { get; set; }

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
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
