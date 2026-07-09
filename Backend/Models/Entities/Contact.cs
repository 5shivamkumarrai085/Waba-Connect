using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Models.Entities;

public class Contact
{
    public int Id { get; set; }
    
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [Required, MaxLength(20)]
    public string Phone { get; set; } = string.Empty;
    
    public ContactType Type { get; set; }
    public ContactStatus Status { get; set; } = ContactStatus.New;
    public ContactSource Source { get; set; }
    
    [MaxLength(100)]
    public string? AssignedTo { get; set; }
    
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ContactGroupMember> GroupMemberships { get; set; } = [];
    public ICollection<CampaignContact> CampaignContacts { get; set; } = [];
}
