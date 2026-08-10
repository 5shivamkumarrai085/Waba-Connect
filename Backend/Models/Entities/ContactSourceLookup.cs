using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// An editable lead source (facebook, whatsapp, saas…). Same two-column design as
/// <see cref="ContactStatusLookup"/>: <see cref="Value"/> is the immutable wire format stored
/// on Contact.Source, <see cref="Name"/> is the editable label.
/// </summary>
public class ContactSourceLookup
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Value { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Hex colour for the source badge, e.g. "#0EA5E9". Same contract as ContactStatusLookup.Color.</summary>
    [MaxLength(9)]
    public string? Color { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsSystem { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
