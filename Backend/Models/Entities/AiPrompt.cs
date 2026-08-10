using System;
using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Helpers;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// A named system prompt defining how the AI assistant behaves.
///
/// <para>
/// Replaces the prompt previously buried in appsettings.json under
/// <c>PersonalAssistants:{name}:Prompt</c>, which could only be changed by editing config and
/// redeploying. Prompts are now selectable per Bot Flow AI node, so different flows can carry
/// different personalities — a support agent and a sales assistant — from the same deployment.
/// </para>
/// </summary>
public class AiPrompt
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The system prompt sent to the model.
    ///
    /// Exempt from the global input sanitizer, which strips anything resembling a tag and
    /// collapses every whitespace run into a single space. Applied to a prompt that silently
    /// destroys multi-line structure and any angle-bracketed examples — degrading the model's
    /// behaviour with no error to explain why.
    /// </summary>
    [Required]
    [SkipSanitization]
    public string PromptText { get; set; } = string.Empty;

    /// <summary>Short summary shown in the list, e.g. "Friendly support agent".</summary>
    [MaxLength(300)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Used when nothing else specifies a prompt. Exactly one row should carry this — the
    /// service enforces it — so the bot router always has something to fall back to.
    /// </summary>
    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
