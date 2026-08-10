using System;
using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Helpers;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// An email template's content and on/off state.
///
/// <para>
/// Management only. This build has no SMTP configuration and no mail service — nothing is ever
/// sent. The templates exist so the copy can be written and reviewed now, and so a future
/// sending implementation has somewhere to read from. The UI says so on the page rather than
/// letting an operator assume mail is flowing.
/// </para>
/// </summary>
public class EmailTemplate
{
    [Key]
    public int Id { get; set; }

    /// <summary>Stable identifier a future sender would look up, e.g. "WelcomeEmail".</summary>
    [Required, MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(300)]
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// HTML body. Exempt from the global sanitizer, which deletes every tag outright — it would
    /// reduce a formatted template to plain text. The service sanitizes deliberately instead,
    /// using the allowlist-based helper.
    /// </summary>
    [Required]
    [SkipSanitization]
    public string BodyHtml { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Comma-separated placeholders this template supports, e.g. "site_name,user_name".</summary>
    [MaxLength(500)]
    public string? AvailableVariables { get; set; }

    /// <summary>Seeded templates a future sender depends on. Protected from deletion.</summary>
    public bool IsSystem { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
