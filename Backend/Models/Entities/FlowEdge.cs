using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

public class FlowEdge
{
    public int Id { get; set; }
    
    public int FlowId { get; set; }
    public BotFlow Flow { get; set; } = null!;

    [Required, MaxLength(100)]
    public string Source { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Target { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? SourceHandle { get; set; }
}
