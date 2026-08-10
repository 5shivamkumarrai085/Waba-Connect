using System;
using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Helpers;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// A saved reply an agent can insert into the chat composer.
/// </summary>
public class CannedReply
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The text inserted into the composer. Exempt from the global sanitizer, which would trim
    /// and collapse whitespace — flattening a deliberately multi-line reply into one line.
    /// </summary>
    [Required]
    [SkipSanitization]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Public replies are available to every agent. Private ones belong to their creator, so
    /// someone can keep personal shortcuts without cluttering the shared list.
    /// </summary>
    public bool IsPublic { get; set; } = true;

    public bool IsActive { get; set; } = true;

    /// <summary>Who created it. Null for replies seeded or created outside a request.</summary>
    public int? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
