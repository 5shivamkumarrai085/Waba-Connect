using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.DTOs.MessageBot;

public class MessageBotResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RelationType { get; set; } = string.Empty;
    public string ReplyText { get; set; } = string.Empty;
    public string ReplyType { get; set; } = string.Empty;
    public string TriggerKeyword { get; set; } = string.Empty;
    public string? Header { get; set; }
    public string? Footer { get; set; }
    public bool IsActive { get; set; }
    public string OptionType { get; set; } = string.Empty;
    
    // Reply Buttons
    public string? Button1 { get; set; }
    public string? Button1Id { get; set; }
    public string? Button2 { get; set; }
    public string? Button2Id { get; set; }
    public string? Button3 { get; set; }
    public string? Button3Id { get; set; }
    
    // CTA URL
    public string? CtaButtonName { get; set; }
    public string? CtaButtonLink { get; set; }
    
    // Files
    public string? FileType { get; set; }
    public string? FileName { get; set; }
    public string? FileUrl { get; set; }
    
    // Assistant
    public string? AssistantName { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateMessageBotRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [Required, MaxLength(50)]
    public string RelationType { get; set; } = "Lead";
    
    [Required, MaxLength(1024)]
    public string ReplyText { get; set; } = string.Empty;
    
    [Required, MaxLength(100)]
    public string ReplyType { get; set; } = "On Exact Match";
    
    [Required, MaxLength(256)]
    public string TriggerKeyword { get; set; } = string.Empty;
    
    [MaxLength(60)]
    public string? Header { get; set; }
    
    [MaxLength(60)]
    public string? Footer { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    [Required, MaxLength(50)]
    public string OptionType { get; set; } = "ReplyButtons";
    
    // Reply Buttons
    [MaxLength(20)]
    public string? Button1 { get; set; }
    [MaxLength(256)]
    public string? Button1Id { get; set; }
    
    [MaxLength(20)]
    public string? Button2 { get; set; }
    [MaxLength(256)]
    public string? Button2Id { get; set; }
    
    [MaxLength(20)]
    public string? Button3 { get; set; }
    [MaxLength(256)]
    public string? Button3Id { get; set; }
    
    // CTA URL
    [MaxLength(20)]
    public string? CtaButtonName { get; set; }
    [MaxLength(2048)]
    public string? CtaButtonLink { get; set; }
    
    // Files
    [MaxLength(50)]
    public string? FileType { get; set; }
    [MaxLength(256)]
    public string? FileName { get; set; }
    [MaxLength(2048)]
    public string? FileUrl { get; set; }
    
    // Assistant
    [MaxLength(100)]
    public string? AssistantName { get; set; }
}

public class UpdateMessageBotRequest : CreateMessageBotRequest
{
}
