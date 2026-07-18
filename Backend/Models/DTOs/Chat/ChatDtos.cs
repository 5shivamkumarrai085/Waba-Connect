namespace WhatsAppCampaignApi.Models.DTOs.Chat;

public class ChatAccountResponse
{
    public int Id { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string VerifiedName { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class ChatConversationResponse
{
    public int Id { get; set; }
    public int ContactId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string LastMessage { get; set; } = string.Empty;
    public int UnreadCount { get; set; }
    public string LastMessageTime { get; set; } = string.Empty;
    public DateTime? LastMessageAt { get; set; }
    public string? AvatarUrl { get; set; }
    public string? FromPhoneNumber { get; set; }
    public string? FromPhoneNumberId { get; set; }
    public string? AssignedTo { get; set; }
    public string? Source { get; set; }
    public DateTime ContactCreatedAt { get; set; }
    public List<string> ContactGroups { get; set; } = [];
    public bool ContactIsActive { get; set; }
}

public class ChatMessageResponse
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsTemplate { get; set; }
    public string? ErrorMessage { get; set; }
    public string? MediaUrl { get; set; }
    public string? MediaType { get; set; }
    public string? MediaFileName { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public int? CampaignId { get; set; }
    public string? WhatsAppMessageId { get; set; }
}

public class SendChatMessageRequest
{
    public string Text { get; set; } = string.Empty;
    public string? FromPhoneNumberId { get; set; }
    public string? MediaUrl { get; set; }
    public string? MediaType { get; set; }
    public string? MediaFileName { get; set; }
}

public class SendTemplateToContactRequest
{
    public int ContactId { get; set; }
    public int TemplateId { get; set; }
    public Dictionary<string, string>? Variables { get; set; }
}
