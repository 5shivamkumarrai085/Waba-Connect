using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

public class CampaignVariable
{
    public int Id { get; set; }
    
    public int CampaignId { get; set; }
    public Campaign Campaign { get; set; } = null!;
    
    [Required, MaxLength(50)]
    public string VariableName { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string? VariableValue { get; set; }
    
    [MaxLength(50)]
    public string? MergeField { get; set; }
}
