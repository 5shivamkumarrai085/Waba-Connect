using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

public class TemplateVariable
{
    public int Id { get; set; }
    
    public int TemplateId { get; set; }
    public Template Template { get; set; } = null!;
    
    public int Position { get; set; }
    
    [MaxLength(200)]
    public string? SampleValue { get; set; }
    
    [MaxLength(200)]
    public string? Description { get; set; }
}
