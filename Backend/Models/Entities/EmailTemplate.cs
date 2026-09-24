using System;
using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Helpers;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// An email template's content and on/off state.
///
/// <para>
/// The single source of truth for every outgoing email, on both paths that send one: the seeded
/// system notifications, and the campaigns authored in the marketing section. There is
/// deliberately no parallel "campaign template" store — two tables holding email copy would drift,
/// and an operator editing the wrong one is a support call that is very hard to diagnose.
/// </para>
/// <para>
/// Placeholders may use either <c>{name}</c> or <c>{{name}}</c>. Both are supported because both
/// exist in practice: the seeded templates were written with single braces, while templates
/// authored through the campaign UI use the double braces the rest of the product uses.
/// MergeFieldRenderer handles both.
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

    /// <summary>
    /// Plain-text alternative to <see cref="BodyHtml"/>.
    ///
    /// <para>
    /// Not cosmetic. A message with no text part is one of the strongest single spam signals
    /// there is, and it is unreadable in any client with HTML disabled. When this is empty the
    /// MIME builder derives one from the HTML, but a hand-written version is always better.
    /// </para>
    /// </summary>
    [SkipSanitization]
    public string? TextBody { get; set; }

    /// <summary>
    /// The short line most clients show after the subject. Left unset, clients fall back to
    /// whatever the body starts with — often "View in browser" or an image alt text.
    /// </summary>
    [MaxLength(300)]
    public string? PreheaderText { get; set; }

    /// <summary>
    /// Language tag, e.g. "en". Mirrors how WhatsApp templates are keyed, and is what lets the
    /// campaign template picker show "Welcome Email (en)".
    /// </summary>
    [MaxLength(10)]
    public string? Language { get; set; }

    /// <summary>What this template is for. Shown in the template list, not sent to anyone.</summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Comma-separated placeholders this template supports, e.g. "site_name,user_name".
    ///
    /// <para>
    /// A declaration, not the truth: the authoritative list is whatever the body actually
    /// references, which MergeFieldRenderer.Extract reports. This field remains because the
    /// seeded templates use it and the editor offers it as insertable chips.
    /// </para>
    /// </summary>
    [MaxLength(500)]
    public string? AvailableVariables { get; set; }

    /// <summary>Seeded templates a future sender depends on. Protected from deletion.</summary>
    public bool IsSystem { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
