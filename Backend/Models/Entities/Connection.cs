using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

public class Connection
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(4)]
    public string? Nickname { get; set; }

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual ICollection<WabaConfiguration> WabaConfigurations { get; set; } = new List<WabaConfiguration>();
    public virtual ICollection<WabaPhoneNumber> WabaPhoneNumbers { get; set; } = new List<WabaPhoneNumber>();
    public virtual ICollection<ChatConversation> ChatConversations { get; set; } = new List<ChatConversation>();
    public virtual ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();
    public virtual ICollection<Campaign> Campaigns { get; set; } = new List<Campaign>();
    public virtual ICollection<MessageBot> MessageBots { get; set; } = new List<MessageBot>();
    public virtual ICollection<TemplateBot> TemplateBots { get; set; } = new List<TemplateBot>();
    public virtual ICollection<BotFlow> BotFlows { get; set; } = new List<BotFlow>();
}
