using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

public class TemplateBot
{
    public int Id { get; set; }
    
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [Required, MaxLength(50)]
    public string RelationType { get; set; } = "Lead"; // "Lead" or "Customer"
    
    public int TemplateId { get; set; }
    public Template Template { get; set; } = null!;
    
    [Required, MaxLength(100)]
    public string ReplyType { get; set; } = "On Exact Match"; // "On Exact Match" or "When Message Contains"
    
    [Required, MaxLength(256)]
    public string TriggerKeyword { get; set; } = string.Empty;
    
    public bool IsActive { get; set; } = true;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    public ICollection<TemplateBotVariable> Variables { get; set; } = [];
}
