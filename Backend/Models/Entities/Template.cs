using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Models.Entities;

public class Template
{
    public int Id { get; set; }
    
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [Required, MaxLength(10)]
    public string Language { get; set; } = "en";
    
    public TemplateCategory Category { get; set; }
    public TemplateType TemplateType { get; set; } = TemplateType.Text;
    public TemplateStatus Status { get; set; } = TemplateStatus.Pending;
    
    [Required, MaxLength(1024)]
    public string BodyText { get; set; } = string.Empty;
    
    public HeaderType HeaderType { get; set; } = HeaderType.None;
    
    [MaxLength(500)]
    public string? HeaderContent { get; set; }
    
    [MaxLength(60)]
    public string? FooterText { get; set; }
    
    [MaxLength(100)]
    public string? WhatsAppTemplateId { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<TemplateVariable> Variables { get; set; } = [];
    public ICollection<Campaign> Campaigns { get; set; } = [];
}
