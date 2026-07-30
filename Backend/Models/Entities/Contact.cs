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
    
    public string? Tags { get; set; }

    // Additional contact details fields
    [MaxLength(200)]
    public string? Email { get; set; }
    
    [MaxLength(200)]
    public string? Company { get; set; }
    
    [MaxLength(500)]
    public string? Website { get; set; }
    
    [MaxLength(100)]
    public string? City { get; set; }
    
    [MaxLength(100)]
    public string? State { get; set; }
    
    [MaxLength(100)]
    public string? Country { get; set; }
    
    [MaxLength(20)]
    public string? ZipCode { get; set; }
    
    [MaxLength(500)]
    public string? Address { get; set; }
    
    [MaxLength(2000)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; } = false;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ContactGroupMember> GroupMemberships { get; set; } = [];
    public ICollection<CampaignContact> CampaignContacts { get; set; } = [];
    public ICollection<ContactNote> Notes { get; set; } = [];
}
