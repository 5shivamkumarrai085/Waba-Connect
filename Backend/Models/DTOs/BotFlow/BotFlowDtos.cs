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

    // Null = fires on all connections. Set = scoped to one connection only.
    public int? ConnectionId { get; set; }
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

    // Null = fires on all connections. Set = scoped to one connection only.
    public int? ConnectionId { get; set; }
    public string? ConnectionName { get; set; }
}
