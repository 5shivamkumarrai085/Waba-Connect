using System;
using WhatsAppCampaignApi.Helpers;

namespace WhatsAppCampaignApi.Models.DTOs.Setup;

public class AiPromptResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PromptText { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SaveAiPromptRequest
{
    public string Name { get; set; } = string.Empty;

    // See AiPrompt.PromptText: the global sanitizer flattens multi-line prompts and deletes
    // angle-bracketed examples, silently degrading how the model behaves.
    [SkipSanitization]
    public string PromptText { get; set; } = string.Empty;

    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }
}

public class CannedReplyResponse
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
    public bool IsActive { get; set; }
    /// <summary>Whether the caller created it — drives whether the UI offers editing.</summary>
    public bool IsMine { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SaveCannedReplyRequest
{
    public string Title { get; set; } = string.Empty;

    [SkipSanitization]
    public string Description { get; set; } = string.Empty;

    public bool IsPublic { get; set; } = true;
    public bool IsActive { get; set; } = true;
}
