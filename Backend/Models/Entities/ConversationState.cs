using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

public class ConversationState
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string PhoneNumber { get; set; } = string.Empty;

    public int FlowId { get; set; }
    public BotFlow Flow { get; set; } = null!;

    [Required, MaxLength(100)]
    public string CurrentNodeId { get; set; } = string.Empty;

    [Required]
    public string VariablesJson { get; set; } = "{}";

    [Required, MaxLength(20)]
    public string Status { get; set; } = "Active"; // Active, Completed

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
