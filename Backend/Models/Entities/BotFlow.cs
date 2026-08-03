using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

public class BotFlow
{
    public int Id { get; set; }
    
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string? Description { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    [Required]
    public string FlowData { get; set; } = "{}"; // JSON canvas elements
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Null = fires on all connections (legacy/global bot). Set = scoped to one connection only.
    public int? ConnectionId { get; set; }
    public Connection? Connection { get; set; }
}
