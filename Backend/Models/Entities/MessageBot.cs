using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

public class MessageBot
{
    public int Id { get; set; }
    
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [Required, MaxLength(50)]
    public string RelationType { get; set; } = "Lead"; // "Lead" or "Customer"
    
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
    public string OptionType { get; set; } = "ReplyButtons"; // "ReplyButtons", "CtaUrl", "Files", "PersonalAssistant"
    
    // Option 1: Reply Buttons
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
    
    // Option 2: CTA URL
    [MaxLength(20)]
    public string? CtaButtonName { get; set; }
    [MaxLength(2048)]
    public string? CtaButtonLink { get; set; }
    
    // Option 3: Files
    [MaxLength(50)]
    public string? FileType { get; set; }
    [MaxLength(256)]
    public string? FileName { get; set; }
    [MaxLength(2048)]
    public string? FileUrl { get; set; }
    
    // Option 4: Personal Assistant
    [MaxLength(100)]
    public string? AssistantName { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
