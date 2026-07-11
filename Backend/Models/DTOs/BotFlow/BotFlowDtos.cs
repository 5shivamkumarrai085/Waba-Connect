using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.DTOs.BotFlow;

public class CreateBotFlowRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string? Description { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    public string? FlowData { get; set; } // Serialized canvas JSON
}

public class UpdateBotFlowRequest : CreateBotFlowRequest
{
}

public class BotFlowResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public string FlowData { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
