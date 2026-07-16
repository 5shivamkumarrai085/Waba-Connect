using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

public class FlowNode
{
    public int Id { get; set; }
    
    public int FlowId { get; set; }
    public BotFlow Flow { get; set; } = null!;

    [Required, MaxLength(100)]
    public string NodeId { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string NodeType { get; set; } = string.Empty;

    public double PositionX { get; set; }
    public double PositionY { get; set; }

    [Required]
    public string DataJson { get; set; } = "{}";
}
