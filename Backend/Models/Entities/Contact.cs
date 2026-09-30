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
    
    /// <summary>
    /// Matches ContactTypeLookup.Value. Stored as a plain string rather than an enum for the
    /// same reason as Status and Source below — an admin adding a type must not require a code
    /// change. The column was already varchar(50) via HasConversion&lt;string&gt;(), so this is a
    /// CLR-side change only and every existing row keeps its value verbatim.
    /// </summary>
    [Required, MaxLength(50)]
    public string Type { get; set; } = "Lead";

    /// <summary>
    /// Matches ContactStatusLookup.Value. Stored as a plain string rather than an enum so
    /// administrators can add and rename statuses without a code change.
    ///
    /// <para>
    /// The column is unchanged by this: the enum was already persisted as varchar(50) through
    /// HasConversion&lt;string&gt;(), so switching the CLR type to string produces an identical
    /// column and needs no data migration. There is deliberately no foreign key — the lookup's
    /// Value is immutable, which gives referential stability without the cascade rules.
    /// </para>
    /// </summary>
    [MaxLength(50)]
    public string Status { get; set; } = "New";

    /// <summary>Matches ContactSourceLookup.Value. Same reasoning as <see cref="Status"/>.</summary>
    [MaxLength(50)]
    public string Source { get; set; } = string.Empty;
    
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

    /// <summary>IANA time zone (e.g. "Asia/Kolkata"). Used for quiet hours and local-time sends; the
    /// account's default time zone applies when unset.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(64)]
    public string? TimeZone { get; set; }

    // ── Click-to-WhatsApp ad attribution (first touch) ─────────────────────────────────────
    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? AdSourceId { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(500)]
    public string? AdSourceUrl { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(300)]
    public string? AdHeadline { get; set; }
    /// <summary>Meta's click id (ctwa_clid), for matching conversions back to the ad.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string? AdClickId { get; set; }
    public DateTime? AdAttributedAt { get; set; }
    
    /// <summary>Date of birth. The age is always computed from it, never stored, so it never goes stale.</summary>
    public DateOnly? DateOfBirth { get; set; }

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
