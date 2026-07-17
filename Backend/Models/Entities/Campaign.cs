using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Models.Entities;

public class Campaign
{
    public int Id { get; set; }
    
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    
    public int TemplateId { get; set; }
    public Template Template { get; set; } = null!;
    
    public ContactType RelationType { get; set; }
    public ScheduleType ScheduleType { get; set; }
    public DateTime? ScheduledAt { get; set; }
    
    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;
    
    public int TotalRecipients { get; set; }
    public int DeliveredCount { get; set; }
    public int ReadCount { get; set; }
    public int FailedCount { get; set; }
    
    [MaxLength(100)]
    public string? CreatedBy { get; set; }
    
    public string? FileName { get; set; }
    public string? FileType { get; set; }
    public string? FileUrl { get; set; }
    
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    
    [MaxLength(100)]
    public string? DeletedBy { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<CampaignContact> CampaignContacts { get; set; } = [];
    public ICollection<CampaignVariable> Variables { get; set; } = [];
}
