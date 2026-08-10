using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// A language available to the app, replacing the hardcoded en/ms/zh list in ContactsController.
/// Feeds the contact, template and user language pickers.
/// </summary>
public class Language
{
    [Key]
    public int Id { get; set; }

    /// <summary>BCP-47-ish short code ("en", "ms", "bm"). Referenced by code, not id, so it is immutable in practice.</summary>
    [Required, MaxLength(10)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Hex colour for the language chip, e.g. "#8B5CF6". Same contract as ContactStatusLookup.Color.</summary>
    [MaxLength(9)]
    public string? Color { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>The fallback language. Exactly one row should carry this.</summary>
    public bool IsDefault { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<Translation> Translations { get; set; } = new List<Translation>();
}
