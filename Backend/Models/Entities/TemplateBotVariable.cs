using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

public class TemplateBotVariable
{
    public int Id { get; set; }
    
    public int TemplateBotId { get; set; }
    public TemplateBot TemplateBot { get; set; } = null!;
    
    [Required, MaxLength(50)]
    public string VariableName { get; set; } = string.Empty; // e.g. "1", "2"
    
    [MaxLength(500)]
    public string? VariableValue { get; set; }
    
    [MaxLength(50)]
    public string? MergeField { get; set; }
}
