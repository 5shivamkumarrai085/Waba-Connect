using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

public class ChatConversation
{
    public int Id { get; set; }

    public int ContactId { get; set; }
    public Contact Contact { get; set; } = null!;

    public int? WabaPhoneNumberId { get; set; }
    public WabaPhoneNumber? WabaPhoneNumber { get; set; }

    [MaxLength(1024)]
    public string? LastMessageText { get; set; }

    public DateTime? LastMessageAt { get; set; }
    public int UnreadCount { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ChatMessage> Messages { get; set; } = [];
}
