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
    
    public int? ConnectionId { get; set; }
    public virtual Connection? Connection { get; set; }
    
    // Comma-separated list of ContactType names (e.g. "Lead,Customer"), not a single
    // enum — a campaign can now target multiple relation types at once. Still a plain
    // VARCHAR(50) column (HasConversion<string>() was removed from the enum mapping in
    // AppDbContext.cs since this is no longer an enum-typed property), so no migration
    // was needed for this change.
    public string RelationType { get; set; } = string.Empty;
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
    public bool IsBulkCampaign { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    
    [MaxLength(100)]
    public string? DeletedBy { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<CampaignContact> CampaignContacts { get; set; } = [];
    public ICollection<CampaignVariable> Variables { get; set; } = [];
}
